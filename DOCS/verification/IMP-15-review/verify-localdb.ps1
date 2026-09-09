param([string]$Configuration = 'Debug', [string]$Version = '17.0')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$instance = 'SqlXmlAnalyzerIMP15Review_' + [Guid]::NewGuid().ToString('N')
$created = $false
$sql = $null
function Query([string]$text, [bool]$scalar = $false) {
    $command = $sql.CreateCommand()
    try {
        $command.CommandText = $text
        if ($scalar) { return $command.ExecuteScalar() }
        $null = $command.ExecuteNonQuery()
    } finally { $command.Dispose() }
}
try {
    & sqllocaldb create $instance $Version -s | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Dedicated LocalDB creation failed.' }
    $created = $true
    $sql = [System.Data.SqlClient.SqlConnection]::new("Data Source=(localdb)\$instance;Initial Catalog=master;Integrated Security=True")
    $sql.Open()
    $serverVersion = Query "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ProductVersion'))" $true
    Query 'CREATE DATABASE [imp15review]'
    $sql.ChangeDatabase('imp15review')
    Query 'CREATE TABLE dbo.T (K int, R int, V int, [@real] int, Expr1000 int, Bmk1000 int, [R]]ange] int, [[literal]]] int)'
    Query 'INSERT dbo.T VALUES (1,2,3,4,5,6,7,8), (1,1,3,4,5,6,7,8)'
    Query 'SET SHOWPLAN_XML ON'
    $command = $sql.CreateCommand()
    try {
        $command.CommandText = 'DECLARE @p int = 1; SELECT K,R,V,[@real],Expr1000,Bmk1000,[R]]ange],[[literal]]] FROM dbo.T WHERE K=@p ORDER BY R'
        $reader = $command.ExecuteReader()
        $xml = ''
        try {
            do {
                while ($reader.Read()) {
                    $value = [string]$reader.GetValue(0)
                    if ($value.Contains('<ShowPlanXML')) { $xml = $value }
                }
            } while ($reader.NextResult())
        } finally { $reader.Dispose() }
        if ($xml.Length -eq 0) { throw 'SQL Server did not return ShowPlan XML.' }
    } finally { $command.Dispose(); Query 'SET SHOWPLAN_XML OFF' }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'captured.sqlplan'), $xml)
    $assemblyDir = Join-Path $repository "SqlXmlAnalyzer.Tests/bin/$Configuration/net8.0-windows"
    foreach ($file in @('Microsoft.SqlServer.TransactSql.ScriptDom.dll','SqlXmlAnalyzer.Core.dll','SqlXmlAnalyzer.dll')) {
        $null = [Reflection.Assembly]::LoadFrom((Join-Path $assemblyDir $file))
    }
    $doc = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe($xml)
    $ns = $doc.Root.Name.Namespace
    $model = [SqlXmlAnalyzer.Core.Services.PlanIdentityAdapter]::GetDocument($doc, [Threading.CancellationToken]::None)
    $op = @($model.Operators | Where-Object { $_.Objects.Count -eq 1 })[0]
    $suggestion = [SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion]::new()
    $suggestion.ObjectIdentity = $op.Objects[0].Identity
    $suggestion.Location = $op.Location
    # An R-leading candidate isolates the Sort contribution from the legacy
    # ScalarString equality heuristic, whose calibration remains in IMP-16.
    $suggestion.KeyColumns.Add([SqlXmlAnalyzer.Core.Models.IndexColumn]@{Name='[R]';Usage='INEQUALITY'})
    $vm = [SqlXmlAnalyzer.ViewModels.IndexSandboxViewModel]::new($suggestion, $doc, $null)
    $expected = @('[@real]','[Bmk1000]','[Expr1000]','[K]','[R]]ange]','[[literal]]]','[V]')
    if (@(Compare-Object $expected @($vm.AvailableColumns)).Count -ne 0) { throw "Unexpected available columns: $($vm.AvailableColumns -join ', ')" }
    if (@([SqlXmlAnalyzer.Core.Services.IndexTargetResolver]::FindSortOrders($suggestion, $doc, $ns)).Count -ne 1) { throw 'Sort identity was lost.' }
    foreach ($name in $expected) { $suggestion.IncludeColumns.Add([SqlXmlAnalyzer.Core.Models.IndexColumn]@{Name=$name;Usage='INCLUDE'}) }
    [SqlXmlAnalyzer.Core.Scoring.IndexScoringCalculator]::CalculateScore($suggestion, $doc, $ns)
    if ($suggestion.Score -ne 55) { throw "Expected 40 coverage + 15 Sort points, got $($suggestion.Score)" }
    $options = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlOptions]::new()
    $options.Online = $false; $options.SortInTempDb = $false; $options.DataCompression = 'ROW'
    $compiled = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler]::Compile($suggestion, $options, $null, $null)
    Query $compiled.Create
    Query $compiled.Rollback
    [ordered]@{Configuration=$Configuration;SqlServerVersion=$serverVersion;AvailableColumns=$expected;ParameterExcluded=$true;CompleteSortCount=1;Score=55;CreateAndRollbackPassed=$true;Passed=$true} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'localdb-results.json') -Encoding utf8
    'Captured plan, sandbox entity columns, Sort, CREATE and rollback verified.'
} finally {
    try {
        if ($sql -and $sql.State -eq 'Open') {
            try { $sql.ChangeDatabase('master'); Query 'DROP DATABASE [imp15review]' } finally { $sql.Dispose() }
        }
    } finally {
        if ($created -and $instance -match '^SqlXmlAnalyzerIMP15Review_[a-f0-9]{32}$') {
            & sqllocaldb stop $instance -k | Out-Null
            & sqllocaldb delete $instance | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Dedicated LocalDB cleanup failed.' }
        }
    }
}
