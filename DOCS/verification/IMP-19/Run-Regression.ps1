param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$results = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot ('database-' + $Configuration.ToLowerInvariant()) }
New-Item -ItemType Directory -Force -Path $results | Out-Null
$cases = [Collections.Generic.List[object]]::new()
foreach ($type in @('varchar(20)','nvarchar(20)','char(20)','nchar(20)')) {
    $cases.Add(@{ Name='ltrim-' + $type.Split('(')[0]; Setup="CREATE TABLE dbo.InputText(Id int, Value $type NULL); INSERT dbo.InputText VALUES(1,N' x'),(2,N'x'),(3,NULL),(4,N'x '),(5,N'  '),(6,N'X');"; Original="SELECT Id FROM dbo.InputText WHERE LTRIM(Value)=N'x';"; Candidate="SELECT Id FROM dbo.InputText WHERE Value=N'x';"; Expected='Different' })
}
$cases.Add(@{Name='trim-leading-trailing'; Setup="CREATE TABLE dbo.InputText(Id int, Value nvarchar(20)); INSERT dbo.InputText VALUES(1,N' x '),(2,NULL),(3,N'x');"; Original="SELECT Id FROM dbo.InputText WHERE TRIM(Value)=N'x';"; Candidate="SELECT Id FROM dbo.InputText WHERE Value=N'x';"; Expected='Different'})
$cases.Add(@{Name='null-and-trailing-control'; Setup="CREATE TABLE dbo.InputText(Id int, Value nvarchar(20)); INSERT dbo.InputText VALUES(1,NULL),(2,N'x'),(3,N'x ');"; Original="SELECT Id FROM dbo.InputText WHERE LTRIM(Value)=N'x';"; Candidate="SELECT Id FROM dbo.InputText WHERE Value=N'x';"; Expected='PassedForScenarios'})
$cases.Add(@{Name='unicode-prefix'; Setup="CREATE TABLE dbo.InputText(Id int, Value nvarchar(20)); INSERT dbo.InputText VALUES(1,N'漢'),(2,N'?');"; Original="SELECT Id FROM dbo.InputText WHERE Value=N'漢';"; Candidate="SELECT Id FROM dbo.InputText WHERE Value='漢';"; Expected='Different'})
$cases.Add(@{Name='case-sensitive-ltrim'; Setup="CREATE TABLE dbo.InputText(Id int, Value nvarchar(20)); INSERT dbo.InputText VALUES(1,N' X'),(2,N'x'),(3,N'X'),(4,NULL);"; Original="SELECT Id FROM dbo.InputText WHERE LTRIM(Value)=N'X';"; Candidate="SELECT Id FROM dbo.InputText WHERE Value=N'X';"; Collation='Latin1_General_100_CS_AS_SC'; Expected='Different'})
$cases.Add(@{Name='commit-control'; Original='DECLARE @t TABLE(Id int); BEGIN TRANSACTION; INSERT @t VALUES(1); COMMIT; SELECT Id FROM @t;'; Candidate='CREATE TABLE #t(Id int); BEGIN TRANSACTION; INSERT #t VALUES(1); COMMIT; SELECT Id FROM #t; DROP TABLE #t;'; Expected='PassedForScenarios'})
$cases.Add(@{Name='rollback'; Original='DECLARE @t TABLE(Id int); BEGIN TRANSACTION; INSERT @t VALUES(1); ROLLBACK; SELECT COUNT(*) AS Total FROM @t;'; Candidate='CREATE TABLE #t(Id int); BEGIN TRANSACTION; INSERT #t VALUES(1); ROLLBACK; SELECT COUNT(*) AS Total FROM #t; DROP TABLE #t;'; Expected='Different'})
$cases.Add(@{Name='nested-rollback'; Original='DECLARE @t TABLE(Id int); BEGIN TRANSACTION; BEGIN TRANSACTION; INSERT @t VALUES(1); COMMIT; ROLLBACK; SELECT COUNT(*) AS Total FROM @t;'; Candidate='CREATE TABLE #t(Id int); BEGIN TRANSACTION; BEGIN TRANSACTION; INSERT #t VALUES(1); COMMIT; ROLLBACK; SELECT COUNT(*) AS Total FROM #t; DROP TABLE #t;'; Expected='Different'})
$cases.Add(@{Name='savepoint'; Original='DECLARE @t TABLE(Id int); BEGIN TRANSACTION; INSERT @t VALUES(1); SAVE TRANSACTION point1; INSERT @t VALUES(2); ROLLBACK TRANSACTION point1; COMMIT; SELECT Id FROM @t;'; Candidate='CREATE TABLE #t(Id int); BEGIN TRANSACTION; INSERT #t VALUES(1); SAVE TRANSACTION point1; INSERT #t VALUES(2); ROLLBACK TRANSACTION point1; COMMIT; SELECT Id FROM #t; DROP TABLE #t;'; Expected='Different'})
$cases.Add(@{Name='try-catch-rollback'; Original="DECLARE @t TABLE(Id int); BEGIN TRY BEGIN TRANSACTION; INSERT @t VALUES(1); THROW 50001, 'test', 1; END TRY BEGIN CATCH ROLLBACK; END CATCH; SELECT COUNT(*) AS Total FROM @t;"; Candidate="CREATE TABLE #t(Id int); BEGIN TRY BEGIN TRANSACTION; INSERT #t VALUES(1); THROW 50001, 'test', 1; END TRY BEGIN CATCH ROLLBACK; END CATCH; SELECT COUNT(*) AS Total FROM #t; DROP TABLE #t;"; Expected='Different'})
$cases.Add(@{Name='declaration-inside-transaction'; Original='BEGIN TRANSACTION; DECLARE @t TABLE(Id int); INSERT @t VALUES(1); ROLLBACK; SELECT Id FROM @t;'; Candidate='BEGIN TRANSACTION; CREATE TABLE #t(Id int); INSERT #t VALUES(1); ROLLBACK; SELECT Id FROM #t;'; Expected='Different'})
$cases.Add(@{Name='existing-name-collision'; Original='CREATE TABLE #t(Id int); DECLARE @t TABLE(Id int); INSERT @t VALUES(1); SELECT Id FROM @t; DROP TABLE #t;'; Candidate='CREATE TABLE #t(Id int); CREATE TABLE #t(Id int); INSERT #t VALUES(1); SELECT Id FROM #t; DROP TABLE #t;'; Expected='Different'})
$cases.Add(@{Name='multiple-declarations'; Original='DECLARE @a TABLE(Id int); DECLARE @b TABLE(Id int); INSERT @a VALUES(1); INSERT @b VALUES(2); SELECT Id FROM @a UNION ALL SELECT Id FROM @b;'; Candidate='CREATE TABLE #a(Id int); CREATE TABLE #b(Id int); INSERT #a VALUES(1); INSERT #b VALUES(2); SELECT Id FROM #a UNION ALL SELECT Id FROM #b; DROP TABLE #a,#b;'; Expected='PassedForScenarios'})
$cases.Add(@{Name='arithmetic-bounded'; Setup='CREATE TABLE dbo.Users(Id int, Age int NULL CHECK(Age BETWEEN -100000 AND 100000)); INSERT dbo.Users VALUES(1,-100000),(2,40),(3,41),(4,100000),(5,NULL);'; Original='SELECT Id FROM dbo.Users WHERE Age + 10 > 50;'; Candidate='SELECT Id FROM dbo.Users WHERE Age > 40;'; Expected='PassedForScenarios'})
$cases.Add(@{Name='arithmetic-overflow'; Setup='CREATE TABLE dbo.Users(Id int, Age int); INSERT dbo.Users VALUES(1,2147483647);'; Original='SELECT Id FROM dbo.Users WHERE Age + 10 > 50;'; Candidate='SELECT Id FROM dbo.Users WHERE Age > 40;'; Expected='Different'})
$cases.Add(@{Name='duplicate-rows'; Setup='CREATE TABLE dbo.InputText(Id int); INSERT dbo.InputText VALUES(1),(1),(2);'; Original='SELECT Id FROM dbo.InputText;'; Candidate='SELECT DISTINCT Id FROM dbo.InputText;'; Expected='Different'})
$cases.Add(@{Name='matching-errors-not-applicable'; Original="SELECT CAST(N'not-int' AS int);"; Candidate="SELECT CAST(N'not-int' AS int);"; Expected='Inconclusive'})
$cases.Add(@{Name='decimal38-binary-control'; Original="SELECT CAST(12345678901234567890123456789012345678 AS decimal(38,0)) AS Amount, CAST(0x00FF AS varbinary(2)) AS Bytes;"; Candidate="SELECT CAST(12345678901234567890123456789012345678 AS decimal(38,0)) AS Amount, CAST(0x00FF AS varbinary(2)) AS Bytes;"; Expected='PassedForScenarios'})
$summary = [Collections.Generic.List[object]]::new()
foreach ($profile in @(@{Version='2019'; Compatibility=150}, @{Version='2025'; Compatibility=170})) {
    foreach ($case in $cases) {
        $stem = Join-Path $results ($profile.Version + '-' + $case.Name)
        $suite = @{InstanceName='SqlXmlAnalyzer_IMP19_' + $profile.Version; CompatibilityLevel=$profile.Compatibility; Collation='Latin1_General_100_CI_AS_SC'; Scenarios=@(@{Name=$case.Name; SetupSql=[string]$case.Setup; ObserveSql=''; OrderedResults=$false})}
        if ($case.Collation) { $suite.Collation = $case.Collation }
        [IO.File]::WriteAllText(($stem + '-original.sql'), $case.Original)
        [IO.File]::WriteAllText(($stem + '-candidate.sql'), $case.Candidate)
        [IO.File]::WriteAllText(($stem + '-suite.json'), ($suite | ConvertTo-Json -Depth 6))
        & dotnet run --no-build -c $Configuration --project (Join-Path $repo 'SqlXmlAnalyzer.CLI') -- semantic-compare ($stem + '-original.sql') --candidate ($stem + '-candidate.sql') --scenarios ($stem + '-suite.json') --output ($stem + '-result.json') > ($stem + '.log') 2>&1
        $exit = $LASTEXITCODE
        $report = Get-Content ($stem + '-result.json') -Raw | ConvertFrom-Json
        $pass = $report.Validation.Status -eq $case.Expected -and $report.Validation.CleanupFailures.Count -eq 0 -and $report.Validation.Errors.Count -eq 0 -and $exit -eq $(if ($case.Expected -eq 'PassedForScenarios') {0} else {1})
        $summary.Add(@{Case=$case.Name; Version=$report.Validation.ServerVersion; Compatibility=$profile.Compatibility; Expected=$case.Expected; Actual=$report.Validation.Status; ExitCode=$exit; Passed=$pass})
        Write-Output "$($profile.Version) $($case.Name): $($report.Validation.Status) [expected $($case.Expected)]"
    }
}
[IO.File]::WriteAllText((Join-Path $results 'summary.json'), ($summary | ConvertTo-Json -Depth 6))
if (@($summary | Where-Object { -not $_.Passed }).Count -ne 0) { throw 'SQL semantic regression failed; inspect summary.json.' }
Write-Output "Passed $($summary.Count)/$($summary.Count) SQL Server cases."
exit 0
