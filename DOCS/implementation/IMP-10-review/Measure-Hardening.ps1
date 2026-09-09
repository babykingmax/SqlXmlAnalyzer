param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$cli = Join-Path $repository "SqlXmlAnalyzer.CLI/bin/$Configuration/net8.0/SqlXmlAnalyzer.CLI.dll"
[void][Reflection.Assembly]::LoadFrom((Join-Path $repository "SqlXmlAnalyzer.Core/bin/$Configuration/net8.0/SqlXmlAnalyzer.Core.dll"))
[SqlXmlAnalyzer.Logger]::Initialize($false, $null, $null, $false)
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-Imp10Measurement-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporary)
try {
    $children = (1..1000 | ForEach-Object { "<RelOp NodeId='$_'/>" }) -join ''
    $measurements = foreach ($variant in @('Normal', 'InvalidLong', 'ZeroPaddedLong')) {
        $raw = switch ($variant) { 'Normal' { '0' }; 'InvalidLong' { '1' * 8192 }; 'ZeroPaddedLong' { '0' * 8192 } }
        $xml = "<ShowPlanXML xmlns='http://schemas.microsoft.com/sqlserver/2004/07/showplan'><BatchSequence><Batch><Statements><StmtSimple><QueryPlan><RelOp NodeId='$raw'><Concat>$children</Concat></RelOp></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>"
        $inputPath = Join-Path $temporary "$variant.sqlplan"
        [IO.File]::WriteAllText($inputPath, $xml)
        $document = [SqlXmlAnalyzer.SafeXmlHelper]::LoadSafe($inputPath)
        $model = [SqlXmlAnalyzer.Core.Services.PlanIdentityAdapter]::GetDocument($document, [Threading.CancellationToken]::None)
        $compact = [Text.Json.JsonSerializer]::Serialize($model, [SqlXmlAnalyzer.Core.Models.PlanDocument], [Text.Json.JsonSerializerOptions]$null)
        $output = (& dotnet $cli read $inputPath 2> (Join-Path $temporary "$variant.stderr.log")) -join [Environment]::NewLine
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) { throw "CLI read failed: $variant ($exitCode)" }
        $result = $output | ConvertFrom-Json -Depth 60
        [pscustomobject][ordered]@{
            Variant = $variant; InputCharacters = $xml.Length; SourceNodeIdCharacters = $raw.Length
            CanonicalNodeId = $model.Operators[0].Key.NodeId; Operators = $model.Operators.Count
            CompactPlanJsonCharacters = $compact.Length; CliJsonCharacters = $output.Length; ExitCode = $exitCode
            Status = $result.Status; Diagnostics = @($model.Diagnostics | ForEach-Object Code)
        }
    }
    $fixture = Join-Path $repository 'SqlXmlAnalyzer.Tests/TestData/imp10_scoped_identities.sqlplan'
    & dotnet $cli read $fixture > (Join-Path $PSScriptRoot 'cli-read.json') 2> (Join-Path $PSScriptRoot 'cli-read.stderr.log')
    $readExit = $LASTEXITCODE
    & dotnet $cli --path $fixture --format json > (Join-Path $PSScriptRoot 'cli-scan.json') 2> (Join-Path $PSScriptRoot 'cli-scan.stderr.log')
    $scanExit = $LASTEXITCODE
    if ($readExit -ne 0 -or $scanExit -ne 0) { throw 'Native CLI fixture smoke failed' }
    [ordered]@{
        MeasuredAt = [DateTimeOffset]::Now.ToString('O'); Configuration = $Configuration
        CliAssemblyHash = (Get-FileHash -LiteralPath $cli -Algorithm SHA256).Hash
        NodeIdMeasurements = @($measurements); FixtureReadExit = $readExit; FixtureScanExit = $scanExit
        Scope = 'Synthetic bounded-input measurement and native CLI smoke; not a production capacity benchmark.'
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'cli-measurement.json') -Encoding utf8
    $measurements | Format-Table Variant, InputCharacters, CompactPlanJsonCharacters, CliJsonCharacters, ExitCode
}
finally {
    [SqlXmlAnalyzer.Logger]::Shutdown()
    $resolved = [IO.Path]::GetFullPath($temporary)
    $prefix = Join-Path ([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) 'SqlXmlAnalyzer-Imp10Measurement-'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected cleanup target' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
