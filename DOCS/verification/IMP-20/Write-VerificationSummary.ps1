param([Parameter(Mandatory)][string]$DebugWpfDirectory, [Parameter(Mandatory)][string]$ReleaseWpfDirectory,
    [ValidateSet('Initial', 'Hardening')][string]$Phase = 'Hardening')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$prefix = if ($Phase -eq 'Hardening') { 'imp20-hardening' } else { 'imp20' }
$expectedTotal = if ($Phase -eq 'Hardening') { 2273 } else { 2258 }
$expectedAdded = if ($Phase -eq 'Hardening') { 44 } else { 29 }
$testPattern = 'WorkspaceSelectionTests|WorkspaceSelectionDiagnosticTests|TypedSelection_ReceivesOriginalEvent'
$screenshots = @('plan-statement-2.png', 'plan-evidence-source.png', 'plan-no-operators.png', 'deadlock-event-2.png', 'deadlock-source.png')
if ($Phase -eq 'Hardening') {
    $testPattern += '|WorkspaceSelectionHardeningTests'
    $screenshots += @('refactoring-notices.png', 'plan-evidence-layout.png', 'deadlock-manual-selection.png')
}
function FileEvidence([string]$path) {
    $absolute = [IO.Path]::GetFullPath($path)
    return [ordered]@{ Path = [IO.Path]::GetRelativePath($repository, $absolute).Replace('\', '/'); Bytes = (Get-Item -LiteralPath $absolute).Length; Sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash }
}
$configurations = foreach ($configuration in @('Debug', 'Release')) {
    $lower = $configuration.ToLowerInvariant()
    $buildPath = Join-Path $repository ".tmp.$prefix-build-$lower.log"
    $testPath = Join-Path $repository ".tmp.$prefix-tests-$lower.log"
    $trxPath = Join-Path $repository ".tmp.$prefix-tests-$lower/$prefix-$lower.trx"
    $build = [IO.File]::ReadAllText($buildPath)
    if ($build -notmatch '0\s+(个警告|Warning)' -or $build -notmatch '0\s+(个错误|Error)') { throw "Build did not pass cleanly: $configuration" }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($trxPath, $settings)
    $trx = [System.Xml.XmlDocument]::new()
    $trx.XmlResolver = $null
    try { $trx.Load($reader) } finally { $reader.Dispose() }
    $counts = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($counts.GetAttribute('total') -ne $expectedTotal -or $counts.GetAttribute('passed') -ne $expectedTotal) { throw "Unexpected test results: $configuration" }
    $newTests = @($trx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object { $_.GetAttribute('testName') -match $testPattern } | ForEach-Object { [ordered]@{ Name = $_.GetAttribute('testName'); Outcome = $_.GetAttribute('outcome') } })
    if ($newTests.Count -ne $expectedAdded) { throw "Unexpected IMP-20 test count: $($newTests.Count)" }
    $wpfDirectory = if ($configuration -eq 'Debug') { $DebugWpfDirectory } else { $ReleaseWpfDirectory }
    $wpfResult = Join-Path $wpfDirectory 'verification.json'
    $wpf = Get-Content -LiteralPath $wpfResult -Raw | ConvertFrom-Json
    if (!$wpf.Passed -or $wpf.BindingErrors -ne 0) { throw "WPF verification failed: $configuration" }
    [ordered]@{
        Configuration = $configuration; BuildWarnings = 0; BuildErrors = 0
        Tests = [ordered]@{ Total = $expectedTotal; Passed = $expectedTotal; Failed = 0; Skipped = 0; Added = $expectedAdded; Cases = $newTests }
        BuildLog = FileEvidence $buildPath; TestLog = FileEvidence $testPath; Trx = FileEvidence $trxPath
        Wpf = [ordered]@{ Passed = $true; BindingErrors = 0; Result = FileEvidence $wpfResult
            Screenshots = $screenshots | ForEach-Object { FileEvidence (Join-Path $wpfDirectory $_) } }
    }
}
$newSources = @('Core/Services/WorkspaceSourceFormatter.cs', 'Core/ViewModels/PlanWorkspaceViewModel.cs', 'Core/ViewModels/DeadlockWorkspaceViewModel.cs', 'Services/PlanWorkspaceUiActionService.cs', 'Services/DeadlockWorkspaceUiActionService.cs', 'SqlXmlAnalyzer.Tests/WorkspaceSelectionTests.cs', 'SqlXmlAnalyzer.Tests/Diagnostics/WorkspaceSelectionDiagnosticTests.cs')
if ($Phase -eq 'Hardening') { $newSources += @('SqlXmlAnalyzer.Tests/WorkspaceSelectionHardeningTests.cs', 'SqlXmlAnalyzer.Tests/TestData/imp20_navigation_branches.sqlplan') }
Push-Location $repository
try {
    $changedSources = @(git -c core.quotePath=false diff --name-only -- '*.cs' '*.xaml')
    $head = git rev-parse HEAD
} finally { Pop-Location }
$summary = [ordered]@{
    Step = 'IMP-20'; Phase = $Phase; VerifiedAt = [DateTimeOffset]::Now.ToString('O'); BaseCommit = $head
    Scope = 'Current uncommitted IMP-20 implementation; existing staged AGENTS.md excluded.'
    Configurations = @($configurations)
    SourceFiles = @(($changedSources + $newSources | Sort-Object -Unique) | ForEach-Object { FileEvidence (Join-Path $repository $_) })
    VerificationScripts = @('Run-WpfVerification.ps1', 'WpfProbe.cs.txt', 'Write-VerificationSummary.ps1') | ForEach-Object { FileEvidence (Join-Path $PSScriptRoot $_) }
    Limits = @('Synthetic repository fixtures; no new live SQL Server concurrency capture.', 'HTML remains a whole-document report; report unification is IMP-22.', 'Selection persistence across application restarts is outside IMP-20.')
}
$outputName = if ($Phase -eq 'Hardening') { 'hardening-summary.json' } else { 'summary.json' }
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $PSScriptRoot $outputName) -Encoding utf8
Write-Output "PASS: verified both configurations, $expectedAdded added cases, production WPF results and screenshot hashes."
