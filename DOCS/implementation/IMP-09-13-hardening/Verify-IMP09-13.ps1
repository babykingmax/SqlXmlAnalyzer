$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$previousVisualOutput = $env:SQLXML_IMP13_VISUAL_OUTPUT
$configurations = @()
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
Push-Location $repoRoot
try {
    foreach ($configuration in @('Debug', 'Release')) {
        $name = $configuration.ToLowerInvariant()
        $output = Join-Path $PSScriptRoot $name
        New-Item -ItemType Directory -Force -Path $output | Out-Null
        dotnet build SqlXmlAnalyzer.sln -c $configuration --no-restore -v minimal 2>&1 |
            Tee-Object -FilePath (Join-Path $output 'build.log')
        $buildExit = $LASTEXITCODE
        if ($buildExit -ne 0) { throw "$configuration build failed: $buildExit" }
        $env:SQLXML_IMP13_VISUAL_OUTPUT = $output
        $trx = "$name-$stamp.trx"
        dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c $configuration --no-build --no-restore -v minimal `
            --logger "trx;LogFileName=$trx" --results-directory $PSScriptRoot 2>&1 |
            Tee-Object -FilePath (Join-Path $output 'test.log')
        $testExit = $LASTEXITCODE
        if ($testExit -ne 0) { throw "$configuration tests failed: $testExit" }
        [xml]$results = Get-Content -LiteralPath (Join-Path $PSScriptRoot $trx) -Raw
        $counts = $results.TestRun.ResultSummary.Counters
        $configurations += [ordered]@{
            Configuration = $configuration; BuildExit = $buildExit; TestExit = $testExit
            Total = [int]$counts.total; Passed = [int]$counts.passed; Failed = [int]$counts.failed
            Trx = $trx
            TestAssemblySha256 = (Get-FileHash "SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Tests.dll").Hash
        }
    }
    $cliResults = @()
    foreach ($name in @('empty-statements', 'opaque-extension')) {
        $statement = if ($name -eq 'empty-statements') { '' } else {
            '<StmtSimple><QueryPlan><InternalInfo><RelOp><RunTimeInformation><RunTimeCountersPerThread/></RunTimeInformation></RelOp></InternalInfo></QueryPlan></StmtSimple>'
        }
        $path = Join-Path $PSScriptRoot "$name.sqlplan"
        $xml = '<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan"><BatchSequence><Batch><Statements>' + $statement + '</Statements></Batch></BatchSequence></ShowPlanXML>'
        [IO.File]::WriteAllText($path, $xml)
        $read = & dotnet SqlXmlAnalyzer.CLI/bin/Release/net8.0/SqlXmlAnalyzer.CLI.dll read $path
        $readExit = $LASTEXITCODE
        $scan = & dotnet SqlXmlAnalyzer.CLI/bin/Release/net8.0/SqlXmlAnalyzer.CLI.dll --path $path --format json
        $scanExit = $LASTEXITCODE
        $read | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$name-read.json") -Encoding utf8
        $scan | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$name-scan.json") -Encoding utf8
        $readObject = ($read -join "`n") | ConvertFrom-Json
        $scanObject = @(($scan -join "`n") | ConvertFrom-Json)[0]
        if ($readExit -ne 0 -or $scanExit -ne 0 -or $readObject.Capabilities -ne 'PlanStatements, PreservedSource' `
            -or $scanObject.Diagnostics.Capabilities -ne $readObject.Capabilities) { throw "CLI contract mismatch: $name" }
        $cliResults += [ordered]@{ Fixture = $name; ReadExit = $readExit; ScanExit = $scanExit; Capabilities = $readObject.Capabilities }
    }
    $sources = @(
        'SqlXmlAnalyzer.Core/Services/PlanCapabilityService.cs',
        'SqlXmlAnalyzer.Core/Services/InputRecognitionService.cs',
        'SqlXmlAnalyzer.Core/Rules/DiagnosticProtocol.cs',
        'Core/Services/DeadlockGraphGeometryService.cs',
        'Core/ViewModels/MainViewModel.cs',
        'SqlXmlAnalyzer.Tests/PlanCapabilityConsistencyTests.cs',
        'SqlXmlAnalyzer.Tests/DeadlockParallelGeometryTests.cs',
        'SqlXmlAnalyzer.Tests/AnalysisInputLifetimeTests.cs',
        'SqlXmlAnalyzer.Tests/DeadlockUnifiedHardeningViewTests.cs'
    )
    [ordered]@{
        TimestampUtc = [DateTime]::UtcNow.ToString('o')
        Configurations = $configurations
        Cli = $cliResults
        Sources = @($sources | ForEach-Object { [ordered]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath $_).Hash } })
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
}
finally {
    $env:SQLXML_IMP13_VISUAL_OUTPUT = $previousVisualOutput
    Pop-Location
}
