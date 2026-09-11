param([Parameter(Mandatory)][string]$DebugWpfDirectory, [Parameter(Mandatory)][string]$ReleaseWpfDirectory)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
function FileEvidence([string]$path) {
    $absolute = [IO.Path]::GetFullPath($path)
    return [ordered]@{ Path = [IO.Path]::GetRelativePath($repository, $absolute).Replace('\', '/'); Bytes = (Get-Item -LiteralPath $absolute).Length; Sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash }
}
$screenshots = @('ab-empty.png', 'ab-comparison.png', 'ab-unmatched.png', 'rewrite-unselected.png', 'rewrite-selected.png', 'rewrite-sql-comparison.png', 'index-dbo.png', 'index-sales.png', 'index-invalid.png', 'index-notification-failed.png', 'index-recovered.png')
$configurations = foreach ($configuration in @('Debug', 'Release')) {
    $lower = $configuration.ToLowerInvariant()
    $buildPath = Join-Path $repository ".tmp.imp21-hardening-build-$lower.log"
    $testPath = Join-Path $repository ".tmp.imp21-hardening-tests-$lower.log"
    $trxPath = Join-Path $repository ".tmp.imp21-hardening-tests-$lower/imp21-hardening-$lower.trx"
    $build = [IO.File]::ReadAllText($buildPath)
    if ($build -notmatch '0\s+(个警告|Warning)' -or $build -notmatch '0\s+(个错误|Error)') { throw "Build failed: $configuration" }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($trxPath, $settings)
    $trx = [System.Xml.XmlDocument]::new(); $trx.XmlResolver = $null
    try { $trx.Load($reader) } finally { $reader.Dispose() }
    $counts = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($counts.GetAttribute('total') -ne '2311' -or $counts.GetAttribute('passed') -ne '2311') { throw "Unexpected test results: $configuration" }
    $cases = @($trx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object { $_.GetAttribute('testName') -match 'ReviewWorkspaceHardeningTests|ExportAfterApply_PreservesCommitStateAndOriginalFailure' } | ForEach-Object { [ordered]@{ Name = $_.GetAttribute('testName'); Outcome = $_.GetAttribute('outcome') } })
    if ($cases.Count -ne 15) { throw "Unexpected IMP-21 hardening test count: $($cases.Count)" }
    $wpfDirectory = if ($configuration -eq 'Debug') { $DebugWpfDirectory } else { $ReleaseWpfDirectory }
    $wpfPath = Join-Path $wpfDirectory 'verification.json'
    $wpf = Get-Content -LiteralPath $wpfPath -Raw | ConvertFrom-Json
    if (!$wpf.Passed -or $wpf.BindingErrors -ne 0) { throw "WPF failed: $configuration" }
    [ordered]@{
        Configuration = $configuration; BuildWarnings = 0; BuildErrors = 0
        Tests = [ordered]@{ Total = 2311; Passed = 2311; Failed = 0; Skipped = 0; Added = 15; Cases = $cases }
        BuildLog = FileEvidence $buildPath; TestLog = FileEvidence $testPath; Trx = FileEvidence $trxPath
        Wpf = [ordered]@{ Passed = $true; BindingErrors = 0; Result = FileEvidence $wpfPath
            Screenshots = $screenshots | ForEach-Object { FileEvidence (Join-Path $wpfDirectory $_) }
            FixturesAndExport = @('plan-a.sqlplan', 'plan-b.sqlplan', 'candidate.sql') | ForEach-Object { FileEvidence (Join-Path $wpfDirectory $_) } }
    }
}
$sources = @('Core/ViewModels/MainViewModel.cs', 'Core/ViewModels/MainViewModel.Comparison.cs', 'Services/PlanComparisonUiActionService.cs',
    'ViewModels/RewriteReviewViewModel.cs', 'ViewModels/IndexSandboxViewModel.cs', 'Views/PlanComparisonWorkspaceView.xaml',
    'Views/RewriteReviewWindow.xaml', 'Views/RewriteReviewWindow.xaml.cs', 'Views/IndexSandboxWindow.xaml', 'Views/IndexSandboxWindow.xaml.cs',
    'SqlXmlAnalyzer.Core/Services/ReviewOutputService.cs', 'SqlXmlAnalyzer.Core/Refactoring/IndexDdlCompiler.cs', 'SqlXmlAnalyzer.Core/Refactoring/IndexDdlOptions.cs',
    'SqlXmlAnalyzer.Tests/ReviewWorkspaceTests.cs', 'SqlXmlAnalyzer.Tests/Diagnostics/ReviewWorkspaceDiagnosticTests.cs',
    'SqlXmlAnalyzer.Tests/ReviewWorkspaceHardeningTests.cs', 'SqlXmlAnalyzer.Tests/RewriteReviewValidationTests.cs')
Push-Location $repository
try { $head = git rev-parse HEAD } finally { Pop-Location }
[ordered]@{
    Step = 'IMP-21 review fixes and hardening'; VerifiedAt = [DateTimeOffset]::Now.ToString('O'); BaseCommit = $head
    Scope = 'Three IMP-21 review fixes on the uncommitted IMP-20/21 worktree. Existing staged AGENTS.md excluded; historical evidence unchanged.'
    BeforeFix = [ordered]@{ Tests = 15; Failed = 15; Passed = 0; Trx = FileEvidence (Join-Path $repository '.tmp.imp21-hardening-red/imp21-hardening-red.trx') }
    Configurations = @($configurations)
    SourceFiles = $sources | ForEach-Object { FileEvidence (Join-Path $repository $_) }
    VerificationScripts = @('Run-WpfVerification.ps1', 'WpfProbe.cs.txt', 'Write-VerificationSummary.ps1') | ForEach-Object { FileEvidence (Join-Path $PSScriptRoot $_) }
    Limits = @('Synthetic ShowPlan and SQL fixtures; no new live-server DDL or performance measurement.', 'Candidate exports do not authorize source writeback. Existing reviewed semantic validation remains mandatory.', 'Version/edition, existing indexes, locks, space and compression support require target-environment review.', 'Unknown-error tests create and validate real minidumps; raw DUMP/log/TRX artifacts are local only.')
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8
Write-Output 'PASS: both configurations; 2311 total/15 added tests each; 22 WPF screenshots and evidence hashes.'
