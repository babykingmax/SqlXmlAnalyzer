param([string]$Configuration = 'Release', [string]$Version = '17.0')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$instance = 'SqlXmlAnalyzerIMP15_' + [Guid]::NewGuid().ToString('N')
$connection = '(localdb)\' + $instance
$scratch = Join-Path $repository ('.tmp.imp15-localdb-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$created = $false
$checks = [Collections.Generic.List[object]]::new()
$utf8 = [Text.UTF8Encoding]::new($true)
function Invoke-VerificationSql([string]$Sql, [bool]$ExpectFailure = $false) {
    $path = Join-Path $scratch ('query-' + [Guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($path, $Sql, $utf8)
    $output = & sqlcmd -S $connection -E -d master -i $path -f 65001 -b -r 1 -l 20 -h -1 -W 2>&1
    $resultCode = $LASTEXITCODE
    if (($ExpectFailure -and $resultCode -eq 0) -or (!$ExpectFailure -and $resultCode -ne 0)) {
        throw "LocalDB assertion failed (exit $resultCode): $output"
    }
    return ($output -join "`n")
}
function SqlLiteral([string]$Value) { return "N'" + $Value.Replace("'", "''") + "'" }
try {
    # This instance is freshly named and belongs only to this verification run.
    & sqllocaldb create $instance $Version -s | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create dedicated LocalDB instance.' }
    $created = $true
    [Reflection.Assembly]::LoadFrom((Join-Path $repository "SqlXmlAnalyzer.Tests/bin/$Configuration/net8.0-windows/Microsoft.SqlServer.TransactSql.ScriptDom.dll")) | Out-Null
    [Reflection.Assembly]::LoadFrom((Join-Path $repository "SqlXmlAnalyzer.Core/bin/$Configuration/net8.0/SqlXmlAnalyzer.Core.dll")) | Out-Null
    $serverVersion = Invoke-VerificationSql "SET NOCOUNT ON; SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ProductVersion'));"
    Invoke-VerificationSql 'CREATE DATABASE [imp15.one]; CREATE DATABASE [imp15.two];' | Out-Null
    $cases = @(
        @{ Database = 'imp15.one'; Schema = 'sales'; Table = 'Order.Detail' },
        @{ Database = 'imp15.one'; Schema = 'audit'; Table = 'Order.Detail' },
        @{ Database = 'imp15.two'; Schema = 'sales'; Table = 'Order.Detail' },
        @{ Database = 'imp15.one'; Schema = 'sch]ema'; Table = 'T]b' },
        @{ Database = 'imp15.one'; Schema = '中文 架構'; Table = '訂單 明細' }
    )
    foreach ($case in $cases) {
        $db = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler]::QuoteIdentifier($case.Database)
        $schema = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler]::QuoteIdentifier($case.Schema)
        $table = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler]::QuoteIdentifier($case.Table)
        Invoke-VerificationSql "USE $db; EXEC(N'CREATE SCHEMA $schema'); CREATE TABLE $schema.$table ([K] int, [R]]ange] int, [載 荷] nvarchar(20));" | Out-Null
        $suggestion = [SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion]::new()
        $suggestion.Database = $db; $suggestion.Schema = $schema; $suggestion.Table = $table
        foreach ($columnInfo in @(@('K','EQUALITY'), @('R]ange','INEQUALITY'), @('載 荷','INCLUDE'))) {
            $column = [SqlXmlAnalyzer.Core.Models.IndexColumn]::new()
            $column.Name = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler]::QuoteIdentifier($columnInfo[0])
            $column.Usage = $columnInfo[1]
            if ($column.Usage -eq 'INCLUDE') { $suggestion.IncludeColumns.Add($column) } else { $suggestion.KeyColumns.Add($column) }
        }
        $options = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlOptions]::new()
        $options.Online = $false; $options.SortInTempDb = $false; $options.DataCompression = 'ROW'
        $compiled = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler]::Compile($suggestion, $options, $null, $null)
        $nameLiteral = SqlLiteral $compiled.IndexName
        $targetLiteral = SqlLiteral "$schema.$table"
        $prefix = "USE $db; DECLARE @objectId int=OBJECT_ID($targetLiteral); DECLARE @indexId int=(SELECT index_id FROM sys.indexes WHERE object_id=@objectId AND name=$nameLiteral);"
        Invoke-VerificationSql $compiled.Create | Out-Null
        Invoke-VerificationSql ($prefix + @'
IF @indexId IS NULL THROW 50010, 'Wrong index target', 1;
IF (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@objectId AND index_id=@indexId) <> 3 THROW 50011, 'Wrong column count', 1;
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=@objectId AND ic.index_id=@indexId AND c.name=N'K' AND ic.key_ordinal=1 AND ic.is_included_column=0) THROW 50012, 'Wrong equality key ordinal', 1;
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=@objectId AND ic.index_id=@indexId AND c.name=N'R]ange' AND ic.key_ordinal=2 AND ic.is_included_column=0) THROW 50013, 'Wrong inequality key ordinal', 1;
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=@objectId AND ic.index_id=@indexId AND c.name=N'載 荷' AND ic.key_ordinal=0 AND ic.is_included_column=1) THROW 50014, 'Wrong include role', 1;
'@) | Out-Null
        Invoke-VerificationSql $compiled.Rollback | Out-Null
        Invoke-VerificationSql ($prefix + "IF @indexId IS NOT NULL THROW 50015, 'Rollback failed', 1;") | Out-Null
        $suggestion.Server = 'IMP15_NOT_THIS_SERVER'
        $guarded = [SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler]::Compile($suggestion, $options, $null, $null)
        $failure = Invoke-VerificationSql $guarded.Create $true
        if ($failure -notmatch 'INDEX_TARGET_SERVER_MISMATCH') { throw 'Server mismatch was not diagnosed.' }
        $guardedName = SqlLiteral $guarded.IndexName
        Invoke-VerificationSql "USE $db; IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=$guardedName) THROW 50016, 'Server guard allowed DDL', 1;" | Out-Null
        $checks.Add([ordered]@{ Database=$case.Database; Schema=$case.Schema; Table=$case.Table; IndexName=$compiled.IndexName; TargetAndColumnOrdinalsVerified=$true; RollbackVerified=$true; WrongServerRejected=$true })
    }
    [ordered]@{
        RecordedUtc=[DateTime]::UtcNow.ToString('o'); Configuration=$Configuration; SqlServerVersion=$serverVersion
        IsolatedLocalDb=$true; ExecutionDatabase='master'; Online=$false; DataCompression='ROW'
        Checks=@($checks.ToArray()); Passed=$true
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'localdb-results.json') -Encoding utf8
    Write-Output "LocalDB verification passed: $($checks.Count) targets; key/include ordinals, rollback and server guards verified."
}
finally {
    if ($created -and $instance -match '^SqlXmlAnalyzerIMP15_[a-f0-9]{32}$') {
        # Remove only the dedicated instance and databases created by this run.
        try { Invoke-VerificationSql 'DROP DATABASE [imp15.one]; DROP DATABASE [imp15.two];' | Out-Null } catch { Write-Warning $_.Exception.Message }
        & sqllocaldb stop $instance -k | Out-Null
        & sqllocaldb delete $instance | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "Dedicated instance cleanup needs attention: $instance" }
    }
}
