param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../..'), [string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$evidence = [System.IO.Directory]::CreateDirectory($OutputDirectory).FullName
$results = @()
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$previousVisualOutput = $env:SQLXML_IMP13_VISUAL_OUTPUT
Push-Location $repository
try {
    foreach ($configuration in @('Debug', 'Release')) {
        $name = $configuration.ToLowerInvariant()
        $trxName = "$name-$runId.trx"
        $env:SQLXML_IMP13_VISUAL_OUTPUT = Join-Path $evidence $name
        dotnet build SqlXmlAnalyzer.sln -c $configuration -v minimal *> (Join-Path $evidence "$name-build.log")
        $buildExit = $LASTEXITCODE
        if ($buildExit -ne 0) { throw "$configuration build failed; see $name-build.log" }
        dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c $configuration --no-build --no-restore -v minimal --logger "trx;LogFileName=$trxName" --results-directory $evidence *> (Join-Path $evidence "$name-test.log")
        $testExit = $LASTEXITCODE
        # A fresh report is mandatory: some test loggers leave an old TRX behind when overwrite fails.
        [xml]$trx = Get-Content -LiteralPath (Join-Path $evidence $trxName) -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        if ([int]$counters.total -le 0) { throw "$configuration generated an empty test report" }
        $results += [ordered]@{Configuration=$configuration; BuildExit=$buildExit; TestExit=$testExit; Total=[int]$counters.total; Passed=[int]$counters.passed; Failed=[int]$counters.failed; Trx=$trxName;
            TestAssemblySha256=(Get-FileHash "SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Tests.dll").Hash}
        if ($testExit -ne 0) { throw "$configuration tests failed; see $name-test.log" }
    }
    $sources = @('SqlXmlAnalyzer.Core/DeadlockCycleAnalysis.cs', 'SqlXmlAnalyzer.Core/DeadlockDiagnostics.cs',
        'SqlXmlAnalyzer.Core/DeadlockGraph.cs', 'SqlXmlAnalyzer.Core/DeadlockXmlParser.cs', 'SqlXmlAnalyzer.Core/DeadlockAnalysisService.cs',
        'SqlXmlAnalyzer.Core/Parsers/DeadlockTimelineParser.cs', 'SqlXmlAnalyzer.Core/Models/DeadlockParseResult.cs', 'SqlXmlAnalyzer.Core/Models/DeadlockEvent.cs',
        'Core/Services/DeadlockGraphLayoutService.cs', 'Core/Services/DeadlockGraphEdgeService.cs', 'Core/Services/DeadlockGraphPlacementService.cs',
        'Core/Services/DeadlockPlaybackStateService.cs', 'Core/Services/DeadlockGraphVisualStateService.cs',
        'Core/Services/DeadlockSelectionDetailService.cs', 'Core/Services/AnalysisReportController.cs', 'Core/Services/HtmlReportActionService.cs',
        'Core/Services/MermaidDiagramService.cs', 'Core/Services/CliService.cs', 'Core/ViewModels/MainViewModel.cs',
        'Services/DeadlockGraphElementUiActionService.cs', 'Services/DeadlockGraphPlaybackVisualService.cs', 'Services/DeadlockPlaybackUiActionService.cs',
        'Services/DeadlockGraphRenderUiActionService.cs', 'Services/DeadlockAnalysisUiActionService.cs', 'Services/ReportExportUiActionService.cs',
        'ViewModels/DeadlockPlaybackViewModel.cs', 'SqlXmlAnalyzer.Tests/DeadlockUnifiedGraphTests.cs',
        'SqlXmlAnalyzer.Tests/Diagnostics/DeadlockUnifiedDiagnosticTests.cs', 'SqlXmlAnalyzer.Tests/DeadlockUnifiedViewTests.cs',
        'SqlXmlAnalyzer.Tests/DeadlockPatternAnalyzerTests.cs', 'SqlXmlAnalyzer.Tests/TestData/imp13_overlapping_deadlock.xdl',
        'Services/DeadlockGraphUiState.cs', 'Core/Services/DeadlockGraphGeometryService.cs', 'SqlXmlAnalyzer.Tests/DeadlockUnifiedHardeningTests.cs',
        'SqlXmlAnalyzer.Tests/DeadlockUnifiedHardeningViewTests.cs', 'DOCS/implementation/IMP-13/Verify-IMP13.ps1',
        'DOCS/implementation/IMP-13-hardening/Measure-IMP13.ps1')
    [ordered]@{TimestampUtc=[DateTime]::UtcNow.ToString('O'); Configurations=$results;
        CoreAssemblySha256=(Get-FileHash SqlXmlAnalyzer.Core/bin/Release/net8.0/SqlXmlAnalyzer.Core.dll).Hash;
        DesktopAssemblySha256=(Get-FileHash bin/Release/net8.0-windows/SqlXmlAnalyzer.dll).Hash;
        Sources=@($sources | ForEach-Object { @{Path=$_;SHA256=(Get-FileHash -LiteralPath $_).Hash} })} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'verification.json') -Encoding utf8
    $results | ConvertTo-Json
}
finally { $env:SQLXML_IMP13_VISUAL_OUTPUT = $previousVisualOutput; Pop-Location }
