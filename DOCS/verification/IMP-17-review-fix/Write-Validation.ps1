# Collect completed IMP-17 review-fix evidence; does not run tests or alter source files.
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
function File-Evidence([string]$relativePath) {
    $absolute = Join-Path $repository $relativePath
    $item = Get-Item -LiteralPath $absolute
    [ordered]@{ Path = $relativePath; Length = $item.Length; SHA256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash }
}
$researchRoot = 'DOCS/verification/IMP-17-review-fix/'
$tree = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'github-sqltools-showplan.txt') -Raw | ConvertFrom-Json
$githubFiles = @(
    @{ File = 'ObjectWrapperTypeConverter.cs'; Path = 'src/Microsoft.SqlTools.ServiceLayer/ExecutionPlan/ShowPlan/ObjectWrapperTypeConverter.cs' },
    @{ File = 'SkeletonManager.cs'; Path = 'src/Microsoft.SqlTools.ServiceLayer/ExecutionPlan/ShowPlan/Comparison/SkeletonManager.cs' }
)
$sources = @(
    [ordered]@{
        Title = 'Microsoft Learn - Compare execution plans'; Status = 'HTTP 200 via read-only HTTPS'
        Url = 'https://learn.microsoft.com/en-us/sql/relational-databases/performance/compare-execution-plans?view=sql-server-ver17'
        Snapshot = File-Evidence ($researchRoot + 'microsoft-compare-plans.txt')
    },
    [ordered]@{
        Title = 'Microsoft SQL Server 2019 Showplan XSD'; Status = 'Direct HTTPS TLS failure; existing repository official-schema copy inspected'
        Url = 'https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd'
        Snapshot = File-Evidence 'SqlXmlAnalyzer.Tests/TestData/Schemas/showplanxml-sql2019.xsd'
    }
)
foreach ($entry in $githubFiles) {
    $blob = $tree.tree | Where-Object { $_.path -eq $entry.Path }
    if (!$blob) { throw "Missing GitHub source entry: $($entry.Path)" }
    $sources += [ordered]@{
        Title = 'Microsoft sqltoolsservice - ' + $entry.File; Status = 'HTTP 200 via read-only HTTPS'
        Url = 'https://github.com/microsoft/sqltoolsservice/blob/main/' + $entry.Path
        BlobSHA = $blob.sha; GitHubTreeSHA = $tree.sha
        Snapshot = File-Evidence ($researchRoot + 'github-' + $entry.File + '.txt')
    }
}
$research = [ordered]@{
    Date = '2026-09-09'; PreferredSourceOrder = @('Microsoft official', 'SQL Server CSS blogs', 'Microsoft CSS cases', 'SQL Server community', 'GitHub mature implementations')
    Method = 'Microsoft official documentation and Microsoft GitHub implementation references were sufficient for these fixes; no exhaustive CSS/community survey is claimed.'
    AccessLimits = @('web search connection failed', 'GitHub connector had no callable interface; used read-only HTTPS', 'Showplan XSD direct URL TLS failure', 'SkeletonNode.cs raw URL TLS failure; not relied upon')
    Sources = $sources
    GitHubTreeSnapshot = File-Evidence ($researchRoot + 'github-sqltools-showplan.txt')
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'research.json'), ($research | ConvertTo-Json -Depth 8))
$sourceFiles = @(
    'SqlXmlAnalyzer.Core/Comparison/PlanComparisonEvidence.cs',
    'SqlXmlAnalyzer.Core/Comparison/PlanComparisonPredicateEvidence.cs',
    'Core/Services/PlanComparisonController.cs', 'Core/Services/TuningSessionService.cs',
    'Core/ViewModels/MainViewModel.cs', 'Core/ViewModels/MainViewModel.Comparison.cs', 'Core/ViewModels/PlanSnapshot.cs',
    'Services/PlanComparisonUiActionService.cs', 'Services/TuningSessionUiActionService.cs',
    'SqlXmlAnalyzer.Tests/PlanComparisonPredicateIdentityTests.cs', 'SqlXmlAnalyzer.Tests/PlanComparisonSelectionIsolationTests.cs',
    'SqlXmlAnalyzer.Tests/PlanComparisonSourceHashTests.cs', 'SqlXmlAnalyzer.Tests/PlanComparisonWorkspaceTests.cs',
    'DOCS/verification/IMP-17/WpfProbe.cs.txt', 'DOCS/verification/IMP-17/Test-WpfProbeFailureHandling.ps1', 'DOCS/verification/IMP-17/verify-wpf.ps1'
)
$documentFiles = @(
    'README.md', 'CHANGELOG.md', 'Architecture.md', 'UserGuide.md', 'DOCS/README.md',
    'DOCS/IMP-17审查修复与加固说明.md', 'DOCS/IMP-17多语句与可比性检查的AB比较.md',
    'DOCS/IMP-17验证探针异常处理修复.md', 'DOCS/软件改善实施规划.md', 'DOCS/软件详细设计与问题分析.md'
)
$wpfDirectories = @{
    Debug = 'DOCS/verification/IMP-17/wpf-Debug-2dbe084c44994adca2eef7946b26336c'
    Release = 'DOCS/verification/IMP-17/wpf-Release-bcd54b06fc754fbb91a7d604a8ae2646'
}
$configurationResults = @()
foreach ($configuration in @('Debug', 'Release')) {
    $suffix = $configuration.ToLowerInvariant()
    $build = Get-Content -LiteralPath (Join-Path $PSScriptRoot "build-final-$suffix.log") -Raw
    $test = Get-Content -LiteralPath (Join-Path $PSScriptRoot "test-final-$suffix.log") -Raw
    if ($build -notmatch '0 个警告' -or $build -notmatch '0 个错误' -or
        $test -notmatch '失败:\s+0，通过:\s+1957，已跳过:\s+0，总计:\s+1957') { throw "Final build/test evidence failed: $configuration" }
    $wpfRelative = $wpfDirectories[$configuration]
    $wpfAbsolute = Join-Path $repository $wpfRelative
    $matrix = Get-Content -LiteralPath (Join-Path $wpfAbsolute 'failure-handling.json') -Raw | ConvertFrom-Json
    $success = Get-Content -LiteralPath (Join-Path $wpfAbsolute 'success/wpf-probe.json') -Raw | ConvertFrom-Json
    if (!$matrix.Passed -or $matrix.Checks -ne 8 -or !$success.Passed -or $success.BindingErrors -ne 0 -or
        !$success.SameSnapshotSelection -or !$success.ManualSessionRoundtrip -or !$success.OriginalHashRestored) { throw "WPF evidence failed: $configuration" }
    $artifacts = @('failure-handling.json', 'success/wpf-probe.json', 'success/comparison-light.png', 'success/comparison-dark.png')
    foreach ($case in $matrix.Records) {
        if (!$case.Passed -or $case.ExitCode -ne $case.ExpectedExitCode) { throw "Process case failed: $configuration/$($case.Case)" }
        $artifacts += $case.Case + '/stdout.log'
        $artifacts += $case.Case + '/stderr.log'
        if ($case.Dump) {
            if ((Get-FileHash -LiteralPath $case.Dump.Path -Algorithm SHA256).Hash -ne $case.Dump.SHA256) { throw 'DUMP hash changed' }
            $stream = [IO.File]::OpenRead($case.Dump.Path)
            try { if ([IO.BinaryReader]::new($stream).ReadUInt32() -ne 0x504D444D) { throw 'DUMP header invalid' } }
            finally { $stream.Dispose() }
            if (!(Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($case.Dump.Path)) 'exception.json'))) { throw 'Missing DUMP sidecar' }
        }
    }
    $configurationResults += [ordered]@{
        Configuration = $configuration; BuildWarnings = 0; BuildErrors = 0
        Tests = @{ Passed = 1957; Failed = 0; Skipped = 0 }
        BuildLog = File-Evidence ($researchRoot + "build-final-$suffix.log")
        TestLog = File-Evidence ($researchRoot + "test-final-$suffix.log")
        WpfBuildAndRunLog = File-Evidence ($researchRoot + "wpf-$suffix.log")
        Wpf = $matrix; WpfBehavior = $success
        WpfArtifacts = @($artifacts | ForEach-Object { File-Evidence ($wpfRelative + '/' + $_) })
    }
}
$manifest = [ordered]@{
    Step = 'IMP-17 review fix'; RecordedAt = [DateTimeOffset]::Now.ToString('O')
    ComparisonModel = 'plan-comparison/1.1.0'; SessionVersion = '2.1'; XmlHashFormat = 'xml-representation/1'
    FixedFindings = @('P1: Seek range identity omitted columns/bounds', 'P2: shared snapshot overwrote A/B selection', 'P2: restored comparison lost original source hash')
    BaselineTests = 1928; AddedTests = 29
    AddedTestBreakdown = @{ PredicateIdentity = 17; SelectionIsolation = 6; SourceHash = 6 }
    Configurations = $configurationResults
    SourceFiles = @($sourceFiles | ForEach-Object { File-Evidence $_ })
    Documentation = @($documentFiles | ForEach-Object { File-Evidence $_ })
    Research = File-Evidence ($researchRoot + 'research.json')
    EvidenceCollector = File-Evidence ($researchRoot + 'Write-Validation.ps1')
    StageLogs = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.log' | ForEach-Object { File-Evidence ($researchRoot + $_.Name) })
    Limitations = @(
        'Intermediate red/green logs and initial test-debug.log contain intentional or subsequently fixed failures; final logs are authoritative.',
        'Predicate checks are targeted structural completeness checks, not full XSD validation or SQL equivalence.',
        'No new SQL Server performance experiment or coverage report; existing native-plan regressions ran in the full suite.',
        'Session fingerprints are recorded provenance, not authentication or rereading the original source file.',
        'Visual inspection covered Debug light and Release light/theme-resource-switch comparison screenshots; no full accessibility audit.',
        'Existing dirty workspace and previous evidence preserved; no commit, push or publication.'
    )
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'validation.json'), ($manifest | ConvertTo-Json -Depth 12))
Write-Output 'Evidence verified: Debug/Release each 1957 tests, 8 WPF cases, native DUMPs and artifact hashes.'
