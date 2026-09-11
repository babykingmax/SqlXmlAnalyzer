param([Parameter(Mandatory)][string]$DebugWpfDirectory, [Parameter(Mandatory)][string]$ReleaseWpfDirectory)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
function FileEvidence([string]$path) {
    $absolute = [IO.Path]::GetFullPath($path)
    [ordered]@{ Path = [IO.Path]::GetRelativePath($repository, $absolute).Replace('\', '/'); Bytes = (Get-Item -LiteralPath $absolute).Length; Sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash }
}
$configurations = foreach ($configuration in @('Debug', 'Release')) {
    $lower = $configuration.ToLowerInvariant()
    $buildPath = Join-Path $repository ".tmp.imp22-build-$lower.log"
    $testPath = Join-Path $repository ".tmp.imp22-tests-$lower.log"
    $trxPath = Join-Path $repository ".tmp.imp22-tests-$lower/imp22-$lower.trx"
    $build = [IO.File]::ReadAllText($buildPath)
    if ($build -notmatch '0\s+(个警告|Warning)' -or $build -notmatch '0\s+(个错误|Error)') { throw "Build failed: $configuration" }
    $settings = [System.Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($trxPath, $settings)
    $trx = [System.Xml.XmlDocument]::new(); $trx.XmlResolver = $null
    try { $trx.Load($reader) } finally { $reader.Dispose() }
    $counts = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($counts.GetAttribute('total') -ne '2342' -or $counts.GetAttribute('passed') -ne '2342') { throw "Unexpected test results: $configuration" }
    $cases = @($trx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object { $_.GetAttribute('testName') -match '(DiagnosticReportTests|UnifiedReportDiagnosticTests)\.' } | ForEach-Object { [ordered]@{ Name = $_.GetAttribute('testName'); Outcome = $_.GetAttribute('outcome') } })
    if ($cases.Count -ne 31) { throw "Unexpected IMP-22 test count: $($cases.Count)" }
    $wpfDirectory = if ($configuration -eq 'Debug') { $DebugWpfDirectory } else { $ReleaseWpfDirectory }
    $wpf = Get-Content -LiteralPath (Join-Path $wpfDirectory 'verification.json') -Raw | ConvertFrom-Json
    $formats = @(Get-Content -LiteralPath (Join-Path $wpfDirectory 'format-verification.json') -Raw | ConvertFrom-Json)
    if (!$wpf.Passed -or $wpf.BindingErrors -ne 0 -or $formats.Count -ne 4 -or @($formats | Where-Object { !$_.Passed }).Count -ne 0) { throw "Output validation failed: $configuration" }
    $artifacts = @(Get-ChildItem -LiteralPath $wpfDirectory -File)
    if (@($artifacts | Where-Object Extension -eq '.png').Count -ne 5) { throw 'Missing WPF screenshots' }
    [ordered]@{
        Configuration = $configuration; BuildWarnings = 0; BuildErrors = 0
        Tests = [ordered]@{ Total = 2342; Passed = 2342; Failed = 0; Skipped = 0; Added = 31; Cases = $cases }
        BuildLog = FileEvidence $buildPath; TestLog = FileEvidence $testPath; Trx = FileEvidence $trxPath
        Wpf = [ordered]@{ Passed = $true; BindingErrors = 0; Reports = $wpf.Reports }
        FormatChecks = $formats
        Artifacts = $artifacts | Sort-Object Name | ForEach-Object { FileEvidence $_.FullName }
        Rendered = Get-ChildItem -LiteralPath (Join-Path $wpfDirectory 'rendered') -File | Sort-Object Name | ForEach-Object { FileEvidence $_.FullName }
    }
}
$sources = @('SqlXmlAnalyzer.Core/Reporting/DiagnosticReport.cs', 'SqlXmlAnalyzer.Core/Reporting/DiagnosticReportFactory.cs',
    'SqlXmlAnalyzer.Core/Reporting/DiagnosticReportRenderer.cs', 'SqlXmlAnalyzer.Core/Reporting/ReportRedactionService.cs',
    'Core/Services/DiagnosticReportExportService.cs', 'Core/ViewModels/PlanWorkspaceViewModel.cs', 'Core/ViewModels/DeadlockWorkspaceViewModel.cs',
    'ViewModels/ReportReviewViewModel.cs', 'Views/ReportReviewWindow.xaml', 'Views/ReportReviewWindow.xaml.cs',
    'MainWindow.ReportEvents.cs', 'SqlXmlAnalyzer.CLI/Program.cs', 'SqlXmlAnalyzer.Tests/DiagnosticReportTests.cs', 'SqlXmlAnalyzer.Tests/Diagnostics/UnifiedReportDiagnosticTests.cs')
$documents = @('Architecture.md', 'UserGuide.md', 'CHANGELOG.md', 'DOCS/README.md', 'DOCS/软件改善实施规划.md', 'DOCS/UI改进和功能改善文档.md', 'DOCS/IMP-22统一报告与脱敏预览.md')
Push-Location $repository
try { $head = git rev-parse HEAD } finally { Pop-Location }
[ordered]@{
    Step = 'IMP-22'; VerifiedAt = [DateTimeOffset]::Now.ToString('O'); BaseCommit = $head
    Scope = 'Unified immutable diagnostic report and redaction preview on the existing uncommitted IMP-20/21 worktree. Staged AGENTS.md preserved.'
    Configurations = @($configurations)
    SourceFiles = $sources | ForEach-Object { FileEvidence (Join-Path $repository $_) }
    Documents = $documents | ForEach-Object { FileEvidence (Join-Path $repository $_) }
    VerificationScripts = @('Run-WpfVerification.ps1', 'WpfProbe.cs.txt', 'Verify-ReportFiles.py', 'Render-WordReports.ps1', 'Write-VerificationSummary.ps1') | ForEach-Object { FileEvidence (Join-Path $PSScriptRoot $_) }
    OfflineBrowser = [ordered]@{ Verified = $false; Reason = 'Browser tool security policy blocked file:// navigation. No alternate browser, localhost server or debugger workaround attempted. Static HTML/CSP/SVG and content checks passed.' }
    Limits = @('Synthetic fixtures; no new SQL execution or performance claim.', 'Report free text is replaced as whole fields; source XML follows the documented supported policy. Before-samples remain local UI only.', 'SVG is graph-only and sqlplan is selection-XML-only, explicitly excluded from full-report equivalence.', 'All PDF pages and Word-rendered pages were generated and reviewed. LibreOffice unavailable; installed Word used read-only after packaged renderer diagnosis.', 'No general browser/DPI/accessibility certification or coverage percentage claimed.')
} | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8
'PASS: both builds; 2342/2342 tests per configuration; 31 new regressions; WPF and cross-format checks. Browser offline open remains blocked.'
