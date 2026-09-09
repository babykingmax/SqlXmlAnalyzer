param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$dir = Join-Path $PSScriptRoot ('database-' + $Configuration.ToLowerInvariant())
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$cli = Join-Path $repo "SqlXmlAnalyzer.CLI/bin/$Configuration/net8.0/SqlXmlAnalyzer.CLI.dll"
$cases = @(
    @{Name='high-surrogates';Original='SELECT CAST(0x00D8 AS nvarchar(1)) AS Value;';Candidate='SELECT CAST(0x01D8 AS nvarchar(1)) AS Value;';Expected='Different'},
    @{Name='low-surrogates';Original='SELECT CAST(0x00DC AS nvarchar(1)) AS Value;';Candidate='SELECT CAST(0x01DC AS nvarchar(1)) AS Value;';Expected='Different'},
    @{Name='replacement-character';Original='SELECT CAST(0x00D8 AS nvarchar(1)) AS Value;';Candidate='SELECT NCHAR(65533) AS Value;';Expected='Different'},
    @{Name='identical-unpaired';Original='SELECT CAST(0x00D8 AS nvarchar(1)) AS Value;';Candidate='SELECT CAST(0x00D8 AS nvarchar(1)) AS Value;';Expected='PassedForScenarios'},
    @{Name='valid-supplementary';Original='SELECT NCHAR(128512) AS Value;';Candidate='SELECT NCHAR(128513) AS Value;';Expected='Different'},
    @{Name='variant-collation';Original="SELECT CAST('a' COLLATE Latin1_General_100_CI_AS AS sql_variant) AS Value;";Candidate="SELECT CAST('a' COLLATE Latin1_General_100_CS_AS AS sql_variant) AS Value;";Expected='Failed'},
    @{Name='variant-basetype';Original="SELECT CAST(CAST('a' AS varchar(1)) AS sql_variant) AS Value;";Candidate="SELECT CAST(CAST(N'a' AS nvarchar(1)) AS sql_variant) AS Value;";Expected='Failed'},
    @{Name='variant-empty';Original='SELECT CAST(NULL AS sql_variant) AS Value WHERE 1=0;';Candidate='SELECT CAST(NULL AS sql_variant) AS Value WHERE 1=0;';Expected='Failed'},
    @{Name='variant-null';Original='SELECT CAST(NULL AS sql_variant) AS Value;';Candidate='SELECT CAST(NULL AS sql_variant) AS Value;';Expected='Failed'},
    @{Name='variant-object';Setup="CREATE TABLE dbo.T(Value sql_variant); INSERT dbo.T VALUES('a');";Original='SELECT 1 AS Value;';Candidate='SELECT 1 AS Value;';Expected='Failed'},
    @{Name='variant-temporary';Setup="CREATE TABLE #T(Value sql_variant); INSERT #T VALUES('a');";Original='SELECT 1 AS Value;';Candidate='SELECT 1 AS Value;';Expected='Failed'},
    @{Name='variant-observer';Original='SELECT 1 AS Value;';Candidate='SELECT 1 AS Value;';Observer="SELECT CAST('a' AS sql_variant) AS Value;";Expected='Failed'},
    @{Name='binary-control';Original='SELECT CAST(0x00D8 AS varbinary(2)) AS Value;';Candidate='SELECT CAST(0x01D8 AS varbinary(2)) AS Value;';Expected='Different'},
    @{Name='ordinary-unicode';Original="SELECT N'漢字' AS Value;";Candidate="SELECT N'漢字' AS Value;";Expected='PassedForScenarios'}
)
$summary = [Collections.Generic.List[object]]::new()
foreach ($profile in @(@{Version='2019';Compatibility=150}, @{Version='2025';Compatibility=170})) {
    foreach ($case in $cases) {
        $stem = Join-Path $dir ($profile.Version + '-' + $case.Name)
        $suite = @{InstanceName='SqlXmlAnalyzer_IMP19_' + $profile.Version;CompatibilityLevel=$profile.Compatibility;Collation='Latin1_General_100_CI_AS_SC';Scenarios=@(@{Name=$case.Name;SetupSql=[string]$case.Setup;ObserveSql=[string]$case.Observer;OrderedResults=$false})}
        [IO.File]::WriteAllText(($stem + '-original.sql'), $case.Original)
        [IO.File]::WriteAllText(($stem + '-candidate.sql'), $case.Candidate)
        [IO.File]::WriteAllText(($stem + '-suite.json'), ($suite | ConvertTo-Json -Depth 6))
        & dotnet $cli semantic-compare ($stem + '-original.sql') --candidate ($stem + '-candidate.sql') --scenarios ($stem + '-suite.json') --output ($stem + '-result.json') > ($stem + '.log') 2>&1
        $code = $LASTEXITCODE
        $result = Get-Content -Raw ($stem + '-result.json') | ConvertFrom-Json
        $report = $result.Validation
        $pass = $report.Status -eq $case.Expected -and $report.CleanupFailures.Count -eq 0 -and $report.Diagnostics.Count -eq 0 -and !$result.SourceWritten -and $code -eq $(if ($case.Expected -eq 'PassedForScenarios') {0} else {1})
        if ($case.Expected -eq 'Failed') { $pass = $pass -and ($report.Errors -join '').Contains('UnsupportedSemanticType:') -and !$report.CanPrepareApply }
        else { $pass = $pass -and $report.Errors.Count -eq 0 }
        $summary.Add(@{Case=$case.Name;Version=$report.ServerVersion;Expected=$case.Expected;Actual=$report.Status;Passed=[bool]$pass})
        Write-Output "$($profile.Version) $($case.Name): $($report.Status), passed=$pass"
    }
    foreach ($kind in @('utf16', 'variant')) {
        $stem = Join-Path $dir ($profile.Version + '-apply-' + $kind)
        $source = $stem + '-source.sql'
        if ($kind -eq 'utf16') {
            $sql = 'SELECT TOP (1) Value FROM dbo.T WHERE N + 1 > 16777216 ORDER BY N;'
            $setup = 'CREATE TABLE dbo.T(N real, Value nvarchar(1)); INSERT dbo.T VALUES(16777216,CAST(0x01D8 AS nvarchar(1))),(16777218,CAST(0x00D8 AS nvarchar(1)));'
            $expected = 'Different'
        } else {
            $sql = 'SELECT Value FROM dbo.T WHERE N + 10 > 50;'
            $setup = "CREATE TABLE dbo.T(N int, Value sql_variant); INSERT dbo.T VALUES(41,CAST('a' AS varchar(1)));"
            $expected = 'Failed'
        }
        [IO.File]::WriteAllText($source, $sql)
        $hash = (Get-FileHash -LiteralPath $source).Hash
        $suite = @{InstanceName='SqlXmlAnalyzer_IMP19_' + $profile.Version;CompatibilityLevel=$profile.Compatibility;Collation='Latin1_General_100_CI_AS_SC';Scenarios=@(@{Name=$kind;SetupSql=$setup;ObserveSql='';OrderedResults=$false})}
        [IO.File]::WriteAllText(($stem + '-suite.json'), ($suite | ConvertTo-Json -Depth 6))
        & dotnet $cli refactor $source --format json --show-sql --output ($stem + '-proposals.json') > ($stem + '-proposals.log') 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Cannot generate production proposal.' }
        $proposal = Get-Content -Raw ($stem + '-proposals.json') | ConvertFrom-Json
        $id = $proposal.Review.Proposals[0].Id
        & dotnet $cli rewrite-validate $source --select $id --scenarios ($stem + '-suite.json') --show-sql --output ($stem + '-validated.json') > ($stem + '-validate.log') 2>&1
        $validateCode = $LASTEXITCODE
        $validated = Get-Content -Raw ($stem + '-validated.json') | ConvertFrom-Json
        $pass = $validateCode -eq 1 -and !$validated.CanApply -and $validated.Validation.Status -eq $expected
        & dotnet $cli rewrite-apply $source --select $id --scenarios ($stem + '-suite.json') --review-source-hash $validated.SourceHash --review-preview-hash $validated.PreviewHash --review-suite-hash $validated.Validation.SuiteHash --acknowledge-scenarios --output ($stem + '-applied.json') > ($stem + '-apply.log') 2>&1
        $applyCode = $LASTEXITCODE
        $applied = Get-Content -Raw ($stem + '-applied.json') | ConvertFrom-Json
        $pass = $pass -and $applyCode -eq 1 -and !$applied.SourceWritten -and $null -eq $applied.Writeback -and $applied.Validation.Status -eq $expected -and (Get-FileHash -LiteralPath $source).Hash -eq $hash -and @(Get-ChildItem -Path ($source + '.SqlXmlAnalyzer-*.bak')).Count -eq 0
        $summary.Add(@{Case='apply-' + $kind;Version=$profile.Version;Expected=$expected;Actual=$applied.Validation.Status;Passed=[bool]$pass})
        Write-Output "$($profile.Version) apply-${kind}: passed=$pass"
    }
}
[IO.File]::WriteAllText((Join-Path $dir 'summary.json'), ($summary | ConvertTo-Json -Depth 6))
if (@($summary | Where-Object { !$_.Passed }).Count) { throw 'Review regression failed; inspect summary.json.' }
Write-Output "Passed $($summary.Count)/$($summary.Count) review checks."
exit 0
