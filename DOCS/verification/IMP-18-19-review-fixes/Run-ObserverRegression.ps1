param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$outputDirectory = Join-Path $PSScriptRoot ('observer-' + $Configuration.ToLowerInvariant())
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$cli = Join-Path $repo "SqlXmlAnalyzer.CLI/bin/$Configuration/net8.0/SqlXmlAnalyzer.CLI.dll"
$fixture = 'CREATE TABLE dbo.T(Id int); INSERT dbo.T VALUES(1),(2),(3);'
$original = 'UPDATE dbo.T SET Id=Id WHERE Id=1; UPDATE dbo.T SET Id=Id WHERE Id IN (2,3);'
$candidate = 'UPDATE dbo.T SET Id=Id WHERE Id IN (2,3); UPDATE dbo.T SET Id=Id WHERE Id=1;'
$cases = @(
    @{Name='rowcount';Setup=$fixture;Original=$original;Candidate=$candidate;Observer='SELECT @@ROWCOUNT AS LastAffected;';Expected='Different';OriginalRows=3;CandidateRows=3;ObserveOriginal=2;ObserveCandidate=1},
    @{Name='rowcount-big';Setup=$fixture;Original=$original;Candidate=$candidate;Observer='SELECT ROWCOUNT_BIG() AS LastAffected;';Expected='Different';ObserveOriginal=2;ObserveCandidate=1},
    @{Name='sequential-observer';Setup=$fixture;Original=$original;Candidate=$candidate;Observer='SELECT @@ROWCOUNT AS First; SELECT @@ROWCOUNT AS Second;';Expected='Different';ObserveOriginal=2;ObserveCandidate=1},
    @{Name='sentinel';Setup=$fixture;Original="UPDATE dbo.T SET Id=Id WHERE Id=1;`nGO`nUPDATE dbo.T SET Id=Id WHERE Id IN (2,3); SELECT 1 AS N;";Candidate="UPDATE dbo.T SET Id=Id WHERE Id IN (2,3); UPDATE dbo.T SET Id=Id WHERE Id IN (2,3);`nGO`nSELECT 1 AS N;";Expected='Different';OriginalRows=3;CandidateRows=4},
    @{Name='select-batch-control';Original="SELECT 1 AS N;`nGO`nSELECT 2 AS N;";Candidate='SELECT 1 AS N; SELECT 2 AS N;';Expected='PassedForScenarios';OriginalRows=-1;CandidateRows=-1},
    @{Name='mixed-batch-control';Setup=$fixture;Original="UPDATE dbo.T SET Id=Id WHERE Id=1;`nGO`nSELECT 1 AS N;";Candidate='UPDATE dbo.T SET Id=Id WHERE Id=1; SELECT 1 AS N;';Expected='PassedForScenarios';OriginalRows=1;CandidateRows=1},
    @{Name='zero-dml';Setup=$fixture;Original='UPDATE dbo.T SET Id=Id WHERE 1=0; SELECT 1 AS N;';Candidate='SELECT 1 AS N;';Expected='Different';OriginalRows=0;CandidateRows=-1},
    @{Name='transaction-control';Setup=$fixture;Original='BEGIN TRANSACTION; UPDATE dbo.T SET Id=Id WHERE Id=1;';Candidate='BEGIN TRANSACTION; UPDATE dbo.T SET Id=Id WHERE Id=1;';Observer='SELECT @@TRANCOUNT, XACT_STATE(), @@OPTIONS;';Expected='PassedForScenarios';TransactionCount=1},
    @{Name='observer-error';Original='SELECT 1 AS N;';Candidate='SELECT 1 AS N;';Observer='SELECT 1/0 AS Invalid;';Expected='Inconclusive'},
    @{Name='keyword-literal-control';Original='SELECT 1 AS N;';Candidate='SELECT 1 AS N;';Observer="SELECT N'DELETE; SELECT INTO; ROLLBACK' AS Text; -- UPDATE dbo.T";Expected='PassedForScenarios'}
)
$summary = [Collections.Generic.List[object]]::new()
foreach ($profile in @(@{Version='2019';Compatibility=150}, @{Version='2025';Compatibility=170})) {
    foreach ($case in $cases) {
        $stem = Join-Path $outputDirectory ($profile.Version + '-' + $case.Name)
        $suite = @{InstanceName='SqlXmlAnalyzer_IMP19_' + $profile.Version;CompatibilityLevel=$profile.Compatibility;Collation='Latin1_General_100_CI_AS_SC';Scenarios=@(@{Name=$case.Name;SetupSql=[string]$case.Setup;ObserveSql=[string]$case.Observer;OrderedResults=$false})}
        [IO.File]::WriteAllText(($stem + '-original.sql'), $case.Original)
        [IO.File]::WriteAllText(($stem + '-candidate.sql'), $case.Candidate)
        [IO.File]::WriteAllText(($stem + '-suite.json'), ($suite | ConvertTo-Json -Depth 6))
        & dotnet $cli semantic-compare ($stem + '-original.sql') --candidate ($stem + '-candidate.sql') --scenarios ($stem + '-suite.json') --output ($stem + '-result.json') > ($stem + '.log') 2>&1
        $code = $LASTEXITCODE
        $result = Get-Content -Raw ($stem + '-result.json') | ConvertFrom-Json
        $actual = $result.Validation
        $pass = $actual.Status -eq $case.Expected -and $actual.Errors.Count -eq 0 -and $actual.CleanupFailures.Count -eq 0 -and !$result.SourceWritten -and $code -eq $(if ($case.Expected -eq 'PassedForScenarios') {0} else {1})
        if ($case.ContainsKey('OriginalRows')) { $pass = $pass -and $actual.Cases[0].Original.Execution.RecordsAffected -eq $case.OriginalRows -and $actual.Cases[0].Candidate.Execution.RecordsAffected -eq $case.CandidateRows }
        if ($case.ContainsKey('ObserveOriginal')) {
            $before = ConvertFrom-Json -InputObject $actual.Cases[0].Original.ObservedState.Results[0].Rows[0] -NoEnumerate
            $after = ConvertFrom-Json -InputObject $actual.Cases[0].Candidate.ObservedState.Results[0].Rows[0] -NoEnumerate
            $pass = $pass -and [int]$before[0][1] -eq $case.ObserveOriginal -and [int]$after[0][1] -eq $case.ObserveCandidate
        }
        if ($case.ContainsKey('TransactionCount')) { $pass = $pass -and $actual.Cases[0].Original.TransactionCount -eq $case.TransactionCount -and $actual.Cases[0].Candidate.TransactionCount -eq $case.TransactionCount }
        $summary.Add(@{Case=$case.Name;Version=$actual.ServerVersion;Expected=$case.Expected;Actual=$actual.Status;Passed=[bool]$pass})
        Write-Output "$($profile.Version) $($case.Name): $($actual.Status), passed=$pass"
    }

    # The production proposal/validate/apply path must block all three reviewed negative workflows.
    foreach ($scenario in @('rowcount', 'mutating-observer', 'readonly-difference')) {
        $stem = Join-Path $outputDirectory ($profile.Version + '-apply-' + $scenario)
        $source = $stem + '-source.sql'
        $text = if ($scenario -eq 'rowcount') {
            'UPDATE dbo.T SET Value=Value WHERE Value + 1 > 16777216; UPDATE dbo.T SET Value=Value WHERE Value + 1 <= 16777216;'
        } else { 'UPDATE dbo.T SET Mark=CASE WHEN Value + 1 > 16777216 THEN 1 ELSE 0 END;' }
        [IO.File]::WriteAllText($source, $text)
        $sourceHash = (Get-FileHash -LiteralPath $source).Hash
        $observer = if ($scenario -eq 'rowcount') {'SELECT @@ROWCOUNT AS LastAffected;'} elseif ($scenario -eq 'mutating-observer') {'DELETE FROM dbo.T;'} else {'SELECT Value, Mark FROM dbo.T;'}
        $suite = @{InstanceName='SqlXmlAnalyzer_IMP19_' + $profile.Version;CompatibilityLevel=$profile.Compatibility;Collation='Latin1_General_100_CI_AS_SC';Scenarios=@(@{Name=$scenario;SetupSql='CREATE TABLE dbo.T(Value real, Mark int); INSERT dbo.T VALUES(16777215,0),(16777216,0),(16777218,0);';ObserveSql=$observer;OrderedResults=$false})}
        [IO.File]::WriteAllText(($stem + '-suite.json'), ($suite | ConvertTo-Json -Depth 6))
        & dotnet $cli refactor $source --format json --show-sql --output ($stem + '-proposals.json') > ($stem + '-proposals.log') 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Cannot generate review proposal.' }
        $proposal = Get-Content -Raw ($stem + '-proposals.json') | ConvertFrom-Json
        $id = $proposal.Review.Proposals[0].Id
        & dotnet $cli rewrite-validate $source --select $id --scenarios ($stem + '-suite.json') --show-sql --output ($stem + '-validated.json') > ($stem + '-validate.log') 2>&1
        $validateCode = $LASTEXITCODE
        if ($scenario -eq 'mutating-observer') {
            $pass = $validateCode -eq 2 -and (Get-Content -Raw ($stem + '-validate.log')).Contains('ObserverReadOnly')
            $sourceReviewHash = 'unused'; $previewHash = 'unused'; $suiteHash = 'unused'
        } else {
            $validated = Get-Content -Raw ($stem + '-validated.json') | ConvertFrom-Json
            $pass = $validateCode -eq 1 -and !$validated.CanApply -and $validated.Validation.Status -eq 'Different'
            $sourceReviewHash = $validated.SourceHash; $previewHash = $validated.PreviewHash; $suiteHash = $validated.Validation.SuiteHash
        }
        & dotnet $cli rewrite-apply $source --select $id --scenarios ($stem + '-suite.json') --review-source-hash $sourceReviewHash --review-preview-hash $previewHash --review-suite-hash $suiteHash --acknowledge-scenarios --output ($stem + '-applied.json') > ($stem + '-apply.log') 2>&1
        $applyCode = $LASTEXITCODE
        if ($scenario -eq 'mutating-observer') { $pass = $pass -and $applyCode -eq 2 -and (Get-Content -Raw ($stem + '-apply.log')).Contains('ObserverReadOnly') }
        else {
            $applied = Get-Content -Raw ($stem + '-applied.json') | ConvertFrom-Json
            $pass = $pass -and $applyCode -eq 1 -and !$applied.SourceWritten -and $null -eq $applied.Writeback -and $applied.Validation.Status -eq 'Different'
        }
        $pass = $pass -and (Get-FileHash -LiteralPath $source).Hash -eq $sourceHash -and @(Get-ChildItem -Path ($source + '.SqlXmlAnalyzer-*.bak')).Count -eq 0
        $summary.Add(@{Case='apply-' + $scenario;Version=$profile.Version;Expected='BlockedWithoutWrite';Actual="validate=$validateCode,apply=$applyCode";Passed=[bool]$pass})
        Write-Output "$($profile.Version) apply-${scenario}: validate=$validateCode, apply=$applyCode, passed=$pass"
    }
}
[IO.File]::WriteAllText((Join-Path $outputDirectory 'summary.json'), ($summary | ConvertTo-Json -Depth 6))
if (@($summary | Where-Object { !$_.Passed }).Count) { throw 'IMP-19 hardening regression failed; inspect summary.json.' }
Write-Output "Passed $($summary.Count)/$($summary.Count) hardening scenarios."
exit 0
