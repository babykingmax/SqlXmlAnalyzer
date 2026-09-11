$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
Push-Location $repository
try {
    $sources = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.cs' '*.csproj' '*.props' '*.sln' '*.xaml' 'RuleConfiguration.json' |
        Where-Object { $_ -notmatch '(^|/)(\.git|bin|obj|\.vs|publish-[^/]*|backups|\.tmp\.[^/]*)(/|$)' } | Sort-Object -Unique)
    $sourceInputs = @($sources | ForEach-Object {
        [ordered]@{ path = $_; bytes = (Get-Item -LiteralPath $_).Length; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    })
    $sourceInputs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'source-inputs.json') -Encoding UTF8
    $configurations = @(foreach ($configuration in @('Debug','Release')) {
        $trxPath = "SqlXmlAnalyzer.Tests/TestResults/imp24-hardening-full-$configuration.trx"
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        if ([int]$counters.failed -ne 0 -or [int]$counters.passed -ne 2549) { throw "Unexpected test result: $configuration" }
        $wpf = Get-Content -LiteralPath (Join-Path $PSScriptRoot "wpf-$configuration/verification.json") -Raw | ConvertFrom-Json
        if (!$wpf.Passed -or !$wpf.CancelledPlanCannotCommit -or !$wpf.ConfigurationPreservesDeadlockRequest) { throw "WPF verification incomplete: $configuration" }
        $build = Get-Content -LiteralPath (Join-Path $PSScriptRoot "build-$configuration.log") -Raw
        if ($build -notmatch '0 个警告' -or $build -notmatch '0 个错误') { throw "Build did not pass cleanly: $configuration" }
        [ordered]@{
            configuration = $configuration
            counters = [ordered]@{ total = [int]$counters.total; passed = [int]$counters.passed; failed = [int]$counters.failed; skipped = [int]$counters.notExecuted }
            build = [ordered]@{ warnings = 0; errors = 0 }
            trx = [ordered]@{ path = $trxPath; sha256 = (Get-FileHash -LiteralPath $trxPath -Algorithm SHA256).Hash }
            focusedTests = @($trx.TestRun.Results.UnitTestResult | Where-Object {
                $_.testName -match 'RuleConfiguration|PlanAnalysisSource|AnalysisSessionCoordinator|TuningSessionUiActionService|DocumentController'
            } | ForEach-Object { [ordered]@{ name = $_.testName; outcome = $_.outcome } })
            wpf = $wpf
        }
    })
    $artifactPaths = @('README.md','WpfProbe.cs.txt','Run-WpfProbe.ps1','MemoryProbe.cs.txt','Run-MemoryProbe.ps1','Capture-Summary.ps1','memory-Release.json','memory-Release.log','source-inputs.json')
    foreach ($configuration in @('Debug','Release')) {
        $artifactPaths += "build-$configuration.log", "tests-$configuration.log", "probe-$configuration.log"
        foreach ($name in @('verification.json','binding-errors.txt','configuration-change-preview.png','workspace-needs-reanalysis.png','workspace-info-override.png','invalid-config-retains-draft.png','save-conflict.png')) {
            $artifactPaths += "wpf-$configuration/$name"
        }
    }
    $summary = [ordered]@{
        task = 'IMP-24 review hardening'
        capturedAtUtc = [DateTime]::UtcNow.ToString('o')
        gitHead = (git rev-parse HEAD)
        workspace = 'Current uncommitted workspace; includes prior IMP-23 and IMP-24 implementation, not a published revision'
        sourceFileCount = $sourceInputs.Count
        newTests = 22
        configurations = $configurations
        memory = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'memory-Release.json') -Raw | ConvertFrom-Json)
        sources = @(
            'https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads',
            'https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-hard-limit',
            'https://github.com/dotnet/runtime/blob/v10.0.3/src/libraries/System.Text.Json/src/System/Text/Json/Reader/JsonReaderHelper.Unescaping.cs'
        )
        artifacts = @($artifactPaths | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash } })
    }
    $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding UTF8
    [pscustomobject]@{ Sources = $sourceInputs.Count; NewTests = 22; Debug = $configurations[0].counters.passed; Release = $configurations[1].counters.passed; WpfPassed = $true; MemoryPassed = $summary.memory.passed }
}
finally { Pop-Location }
