param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$instance = 'SqlXmlAnalyzerIMP16Review_' + [Guid]::NewGuid().ToString('N')
$created = $false
$databaseCreated = $false
$storage = Join-Path $repository ('.tmp.imp16-localdb-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $storage | Out-Null
$connection = $null
function Execute([string]$query) {
    $command = $connection.CreateCommand()
    try { $command.CommandText = $query; $null = $command.ExecuteNonQuery() } finally { $command.Dispose() }
}
try {
    & sqllocaldb create $instance '17.0' -s | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Dedicated LocalDB creation failed' }
    $created = $true
    $connection = [System.Data.SqlClient.SqlConnection]::new("Data Source=(localdb)\$instance;Initial Catalog=master;Integrated Security=True")
    $connection.Open()
    $version = $connection.ServerVersion
    $dataFile = (Join-Path $storage 'imp16review.mdf').Replace("'", "''")
    $logFile = (Join-Path $storage 'imp16review_log.ldf').Replace("'", "''")
    Execute "CREATE DATABASE [imp16review] ON PRIMARY (NAME=N'imp16review',FILENAME=N'$dataFile') LOG ON (NAME=N'imp16review_log',FILENAME=N'$logFile')"
    $databaseCreated = $true
    $connection.ChangeDatabase('imp16review')
    Execute 'CREATE TABLE dbo.T(K int NULL, V int NULL, R int NULL, [@real] int NULL); INSERT dbo.T VALUES(-1,1,2,3),(1,2,3,4),(NULL,NULL,3,NULL)'
    Execute 'SET SHOWPLAN_XML ON'
    $command = $connection.CreateCommand()
    try {
        $command.CommandText = @'
SELECT K FROM dbo.T WHERE K = -1 OPTION (RECOMPILE);
SELECT K FROM dbo.T WHERE K IS NULL OPTION (RECOMPILE);
SELECT K FROM dbo.T WHERE K IS NOT NULL OPTION (RECOMPILE);
SELECT K FROM dbo.T WHERE CASE WHEN K=1 THEN V ELSE R END = 3 OPTION (RECOMPILE);
SELECT K FROM dbo.T WHERE K=-1 AND CASE WHEN V=1 THEN R ELSE V END = 3 OPTION (RECOMPILE);
DECLARE @p int = 1; SELECT K FROM dbo.T WHERE K=@p;
SELECT K FROM dbo.T WHERE K=[@real];
SELECT K FROM dbo.T WHERE K=-1;
'@
        $reader = $command.ExecuteReader()
        $plans = [Collections.Generic.List[string]]::new()
        try { do { while ($reader.Read()) { $value = [string]$reader.GetValue(0); if ($value.Contains('<ShowPlanXML')) { $plans.Add($value) } } } while ($reader.NextResult()) }
        finally { $reader.Dispose() }
    } finally { $command.Dispose(); Execute 'SET SHOWPLAN_XML OFF' }
    if ($plans.Count -ne 1) { throw "Expected one batch plan, got $($plans.Count)" }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'captured.sqlplan'), $plans[0])
    $assemblyDirectory = Join-Path $repository "SqlXmlAnalyzer.Tests/bin/$Configuration/net8.0-windows"
    foreach ($file in @('Microsoft.SqlServer.TransactSql.ScriptDom.dll','SqlXmlAnalyzer.Core.dll')) {
        [Reflection.Assembly]::LoadFrom((Join-Path $assemblyDirectory $file)) | Out-Null
    }
    $document = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe($plans[0])
    $ns = $document.Root.Name.Namespace
    $model = [SqlXmlAnalyzer.Core.Services.PlanIdentityAdapter]::GetDocument($document,[Threading.CancellationToken]::None)
    # RECOMPILE exposes direct literals for the regression cases. The final query
    # deliberately retains automatic parameterization/ConstExpr conversion, which remains unsupported.
    $expectedEquality = @(30,30,0,0,30,30,0,0)
    $expectedRange = @(0,0,15,0,0,0,0,0)
    $operators = @($model.Operators | Where-Object { $_.Objects.Count -eq 1 })
    if ($operators.Count -ne 8) { throw "Unexpected access operator count: $($operators.Count)" }
    $records = for ($i=0; $i -lt $operators.Count; $i++) {
        $operator = $operators[$i]
        $candidate = [SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion]::new()
        $candidate.ObjectIdentity = $operator.Objects[0].Identity
        $candidate.Location = $operator.Location
        $candidate.KeyColumns.Add([SqlXmlAnalyzer.Core.Models.IndexColumn]@{Name='[K]';Usage='EQUALITY'})
        $score = [SqlXmlAnalyzer.Core.Scoring.IndexScoringCalculator]::Evaluate($candidate,$document,$ns,$null)
        if ($score.EqualityPoints -ne $expectedEquality[$i] -or $score.InequalityPoints -ne $expectedRange[$i]) { throw "Predicate ${i}: unexpected score $($score.Breakdown)" }
        [ordered]@{Case=$i+1;Equality=$score.EqualityPoints;Range=$score.InequalityPoints;Score=$score.Score;ModelVersion=$score.ModelVersion;EvidenceSource=$score.EvidenceSource}
    }
    [ordered]@{Passed=$true;ServerVersion=$version;Configuration=$Configuration;Plans=$records;Scope='Dedicated LocalDB synthetic table, optimizer plans; no performance calibration.'} |
        ConvertTo-Json -Depth 7 | Set-Content (Join-Path $PSScriptRoot 'localdb-results.json') -Encoding utf8
    Write-Output "PASS: eight native SQL Server predicate plans ($version), including an explicitly unsupported converted value."
} finally {
    try {
        if ($databaseCreated -and $connection.State -eq 'Open') {
            $connection.ChangeDatabase('master')
            Execute 'ALTER DATABASE [imp16review] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [imp16review]'
        }
    } finally {
        if ($connection) { $connection.Dispose(); [System.Data.SqlClient.SqlConnection]::ClearAllPools() }
        if ($created) {
            & sqllocaldb stop $instance -k | Out-Null
            & sqllocaldb delete $instance | Out-Null
        }
        # SQL DROP removes its data/log files. Delete only the now-empty workspace directory.
        [IO.Directory]::Delete($storage, $false)
    }
}
