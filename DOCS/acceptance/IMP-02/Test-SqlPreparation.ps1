#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../..'))
$ErrorActionPreference='Stop'
$repository=(Resolve-Path -LiteralPath $RepositoryPath).Path
# Read this known build output only; no enumeration of bin/obj or database connection.
$assemblyPath=Join-Path $repository 'bin/Debug/net8.0-windows/Microsoft.SqlServer.TransactSql.ScriptDom.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) { throw 'Build the WPF project in Debug first.' }
$assembly=[Reflection.Assembly]::LoadFrom($assemblyPath)
$parser=[Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser]::new($true)
$names=@('00-PrepareIsolatedDatabase.sql','10-SemanticCounterexamples.sql','20-CaptureActualPlans.sql','30-DeadlockSessionA.sql','31-DeadlockSessionB.sql','90-CleanupFixtureObjects.sql')
$results=@(foreach($name in $names) {
    $path=Join-Path $PSScriptRoot "sql/$name"
    $reader=[IO.StringReader]::new([IO.File]::ReadAllText($path))
    try {
        $parseErrors=$null
        $null=$parser.Parse($reader,[ref]$parseErrors)
        [pscustomobject]@{ path="sql/$name"; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; parserErrors=@($parseErrors | ForEach-Object { [pscustomobject]@{ line=$_.Line; column=$_.Column; message=$_.Message } }); serverExecuted=$false }
    } finally { $reader.Dispose() }
})
$record=[ordered]@{ checkedUtc=[DateTimeOffset]::UtcNow.ToString('o'); parser='TSql160Parser'; assemblyVersion=$assembly.GetName().Version.ToString(); results=$results; limitation='Syntax only. No database preparation, SQL execution, permissions, runtime behavior or equivalence verified.' }
$name='sql-preparation-'+[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)+'.json'
$output=Join-Path $PSScriptRoot $name
[IO.File]::WriteAllText($output,($record|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
$record|ConvertTo-Json -Depth 10
Write-Host "Syntax evidence: $output"
if (@($results | Where-Object { $_.parserErrors.Count -gt 0 }).Count -gt 0) { exit 1 }
