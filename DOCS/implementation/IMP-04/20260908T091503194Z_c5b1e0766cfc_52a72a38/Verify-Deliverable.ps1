param([string]$RepositoryPath = 'E:/SqlXmlAnalyzer')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath($RepositoryPath)
$debug = Join-Path $repository 'DOCS/acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1'
$release = Join-Path $PSScriptRoot 'release-final-02'
$processRun = Join-Path $PSScriptRoot 'process-final-02'
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'deliverable-validation.json')) { throw 'Immutable evidence already exists.' }
function Read-Json([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
function Save-Json($value, [string]$path) { ConvertTo-Json -InputObject $value -Depth 18 | Set-Content -LiteralPath $path -Encoding utf8 }
function Verify-Manifest([string]$relativePath) {
    $manifest = Join-Path $repository $relativePath
    $base = Split-Path -Parent $manifest
    $entries = @(Read-Json $manifest)
    $failures = @(foreach ($entry in $entries) {
        $path = Join-Path $base $entry.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { "Missing: $path" }
        elseif ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry.sha256) { "Changed: $path" }
    })
    [ordered]@{ manifest = $relativePath; entries = $entries.Count; failures = $failures; passed = $failures.Count -eq 0 }
}
$manifestChecks = @(
    'DOCS/baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/artifact-manifest.json',
    'DOCS/acceptance/IMP-02/runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/artifact-manifest.json',
    'DOCS/acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/artifact-manifest.json',
    'DOCS/acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/artifact-manifest.json',
    'DOCS/implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/artifact-manifest.json',
    'DOCS/acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/artifact-manifest.json'
) | ForEach-Object { Verify-Manifest $_ }
$reviewPaths = @('DOCS/review-evidence/ReviewProbe.cs.txt', 'DOCS/review-evidence/Run-ReviewProbe.ps1', 'DOCS/review-evidence/baseline.json', 'DOCS/review-evidence/probe-results.json')
Import-Module (Join-Path $debug 'adapter/AcceptanceInfrastructure.psm1') -Force
$before = Read-Json (Join-Path $PSScriptRoot 'input-snapshot-before.json')
$current = Get-AcceptanceInputSnapshot -RepositoryPath $repository -AdditionalPaths $reviewPaths
$currentNoReview = [pscustomobject]@{ head = $current.head; inputs = @($current.inputs | Where-Object { $_.path -notin $reviewPaths }) }
$sinceDebug = @(Compare-AcceptanceInputSnapshot -Before (Read-Json (Join-Path $debug 'input-snapshot-before.json')) -After $current)
$sinceRelease = @(Compare-AcceptanceInputSnapshot -Before (Read-Json (Join-Path $release 'input-after.json')) -After $currentNoReview)
$changes = @(Compare-AcceptanceInputSnapshot -Before $before -After $current)
$owned = @(
    '.gitignore', 'App.xaml.cs', 'Core/Services/PlanAnalysisService.cs', 'SqlXmlAnalyzer.CLI/Program.cs',
    'SqlXmlAnalyzer.Core/Logger.cs', 'SqlXmlAnalyzer.Core/Models/RefactorResult.cs', 'SqlXmlAnalyzer.Core/RefactorContext.cs',
    'SqlXmlAnalyzer.Core/Models/RefactorSafetySkip.cs', 'SqlXmlAnalyzer.Core/Diagnostics/DiagnosticFileAccess.cs',
    'SqlXmlAnalyzer.Core/Diagnostics/ExceptionPolicy.cs', 'SqlXmlAnalyzer.Core/Diagnostics/GlobalExceptionHandlers.cs',
    'SqlXmlAnalyzer.Core/Diagnostics/UnexpectedErrorReporter.cs', 'SqlXmlAnalyzer.Core/Diagnostics/WindowsMiniDumpWriter.cs',
    'SqlXmlAnalyzer.Tests/Application/ApplicationOrchestratorTests.cs', 'SqlXmlAnalyzer.Tests/Application/CliProgramTests.cs',
    'SqlXmlAnalyzer.Tests/Application/RefactorSafetyIntegrationTests.cs', 'SqlXmlAnalyzer.Tests/Diagnostics/DiagnosticLoggingTests.cs',
    'SqlXmlAnalyzer.Tests/Refactoring/SqlRefactoringEngineTests.cs', 'SqlXmlAnalyzer.Tests/Refactoring/UnsafeRewriteProtectionTests.cs',
    'src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs', 'src/SqlXmlAnalyzer.Application/Models/RefactorReportDto.cs',
    'src/SqlXmlAnalyzer.Application/Services/ApplicationLoggerProvider.cs', 'src/SqlXmlAnalyzer.Application/Services/ConsoleResultReporter.cs',
    'src/SqlXmlAnalyzer.Application/Services/JsonResultReporter.cs', 'src/SqlXmlAnalyzer.Application/Services/RefactorPresentation.cs',
    'src/SqlXmlAnalyzer.Refactoring/Rules/TableVariableRefactorRule.cs', 'src/SqlXmlAnalyzer.Refactoring/Rules/TrimRefactorRule.cs',
    'src/SqlXmlAnalyzer.Refactoring/SqlRefactoringEngine.cs'
)
$unexpected = @($changes | Where-Object { $_.path -notin $owned })
$userPaths = @('AGENTS.md', 'MainWindow.Services.cs', 'MainWindow.KeyboardShortcuts.cs', 'Views/ShellNavigationRail.xaml')
$retained = @(foreach ($path in @($userPaths + $reviewPaths)) {
    $old = @($before.inputs | Where-Object path -CEQ $path)[0]
    $now = @($current.inputs | Where-Object path -CEQ $path)[0]
    [ordered]@{ path = $path; beforeSha256 = $old.sha256; afterSha256 = $now.sha256; unchanged = $old.sha256 -ceq $now.sha256 }
})
$documents = @('DOCS/IMP-04高风险改写限制与诊断说明.md', 'DOCS/软件改善实施规划.md', 'DOCS/IMP-02审查反例验收矩阵.md', 'DOCS/acceptance/IMP-02/README.md')
$adapter = @('DOCS/acceptance/IMP-02/AcceptanceProbe.cs.txt', 'DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1', 'DOCS/acceptance/IMP-02/acceptance-matrix.json')
Save-Json ([ordered]@{ head = $current.head; baseline = 'input-snapshot-before.json'; sourceAndConfiguration = $changes; documentChanges = $documents; adapterChanges = $adapter; preservedInputs = $retained; unexpectedChanges = $unexpected; committed = $false; note = 'The tracked Git diff is relative to HEAD and includes earlier IMP-03 edits in shared files. Before/after hashes here identify this step.' }) (Join-Path $PSScriptRoot 'change-ownership.json')
Save-Json $current (Join-Path $PSScriptRoot 'input-snapshot-final.json')
$snapshots = @(foreach ($path in @($owned + $documents + $adapter)) {
    $destination = Join-Path $PSScriptRoot ('source-snapshots/' + $path + '.snapshot')
    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    [IO.File]::Copy((Join-Path $repository $path), $destination, $false)
    [ordered]@{ source = $path; snapshot = [IO.Path]::GetRelativePath($PSScriptRoot, $destination); sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
})
Save-Json $snapshots (Join-Path $PSScriptRoot 'source-snapshot-manifest.json')
git -C $repository -c core.quotepath=false ls-files 1> (Join-Path $PSScriptRoot 'git-ls-files.txt')
if ($LASTEXITCODE -ne 0) { throw 'git inventory failed' }
git -C $repository -c core.quotepath=false status --short 1> (Join-Path $PSScriptRoot 'git-status-final.txt')
if ($LASTEXITCODE -ne 0) { throw 'git status failed' }
git -C $repository diff --check -- @owned 1> (Join-Path $PSScriptRoot 'diff-check.stdout.txt') 2> (Join-Path $PSScriptRoot 'diff-check.stderr.txt')
$diffExit = $LASTEXITCODE
git -C $repository diff --no-ext-diff -- @owned 1> (Join-Path $PSScriptRoot 'tracked-scope.diff') 2> (Join-Path $PSScriptRoot 'tracked-diff.stderr.txt')
if ($LASTEXITCODE -ne 0) { throw 'git diff failed' }
$debugSummary = Read-Json (Join-Path $debug 'summary.json')
$releaseSummary = Read-Json (Join-Path $release 'summary.json')
$processSummary = Read-Json (Join-Path $processRun 'summary.json')
$dumpRecords = @(foreach ($result in $processSummary.results | Where-Object scenario -EQ 'unknown-error') {
    $path = $result.verification.Diagnostic.DumpPath
    $metadata = $result.verification.Diagnostic.MetadataPath
    [ordered]@{ configuration = $result.configuration; path = [IO.Path]::GetRelativePath($PSScriptRoot, $path); bytes = [IO.FileInfo]::new($path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; metadata = [IO.Path]::GetRelativePath($PSScriptRoot, $metadata); signature = $result.verification.DumpSignature; streams = $result.verification.DumpStreamCount }
})
Save-Json $dumpRecords (Join-Path $PSScriptRoot 'dump-manifest.json')
$encodingChecks = @(foreach ($configuration in @('debug', 'release')) {
    $old = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "process-final/$configuration/risk-preserved.stdout.txt"))
    $now = [IO.File]::ReadAllText((Join-Path $processRun "$configuration/risk-preserved.stdout.txt"))
    [ordered]@{ configuration = $configuration; oldStructuralCheckPassed = $true; oldChineseTextCorrupted = $old.Contains([char]0xFFFD); finalChineseTextPreserved = $now.Contains('已跳过') -and $now.Contains('回滚') -and -not $now.Contains([char]0xFFFD) }
})
Save-Json ([ordered]@{ classification = 'Current-step development findings, not historical baseline failures'; firstBuild = 'One nullable warning fixed'; firstTargetedTests = '145 passed, 2 failed: log read sharing and parser position message; second attempt 147 passed'; initialFullDebug = '884 passed before outcome tests were added'; firstProcessCheck = '8 structural assertions passed but did not detect Chinese encoding corruption; UTF-8 fixed and assertions strengthened'; encodingVerification = $encodingChecks }) (Join-Path $PSScriptRoot 'intermediate-results.json')
$validation = [ordered]@{
    step = 'IMP-04'; timestampUtc = [DateTime]::UtcNow.ToString('o'); head = $current.head; debugRun = $debugSummary.runId;
    historyAndFinalArtifactChecks = @($manifestChecks); retainedInputs = $retained; unexpectedChanges = $unexpected;
    sourceDriftSinceDebug = $sinceDebug; sourceDriftSinceRelease = $sinceRelease; ownedSourceAndConfigurationFiles = $owned.Count;
    debug = $debugSummary.existingSuite; release = $releaseSummary; infrastructureTests = $debugSummary.infrastructureTests;
    processValidation = [ordered]@{ total = $processSummary.total; passed = $processSummary.passed; encoding = $encodingChecks };
    dumps = $dumpRecords; probeConditionsMet = $debugSummary.probeConditionsMet; remainingNotMet = $debugSummary.notMet;
    diffCheckExitCode = $diffExit; issuesAutomaticallyClosed = 0; sqlServerExecutionPerformed = $false; wpfVisualTestPerformed = $false; coverageGenerated = $false;
    passed = @($manifestChecks | Where-Object { -not $_.passed }).Count -eq 0 -and @($retained | Where-Object { -not $_.unchanged }).Count -eq 0 -and $unexpected.Count -eq 0 -and $sinceDebug.Count -eq 0 -and $sinceRelease.Count -eq 0 -and $debugSummary.existingSuite.passed -eq 886 -and $releaseSummary.passed -eq 886 -and $processSummary.passed -eq 8 -and $debugSummary.infrastructureTests.passed -eq 32 -and @($encodingChecks | Where-Object { -not $_.finalChineseTextPreserved }).Count -eq 0 -and $diffExit -eq 0
}
Save-Json $validation (Join-Path $PSScriptRoot 'deliverable-validation.json')
# Git supplies the inventory. Known ignored logs, dumps and backups are added explicitly.
$relative = [IO.Path]::GetRelativePath($repository, $PSScriptRoot).Replace('\', '/')
$gitPaths = @(git -C $repository -c core.quotepath=false ls-files --others --exclude-standard -- $relative)
if ($LASTEXITCODE -ne 0) { throw 'Evidence inventory failed' }
$all = @($gitPaths | ForEach-Object { Join-Path $repository $_ })
$all += @('build-development-01.log', 'targeted-debug-01.log', 'targeted-debug-02.log', 'full-debug-development-01.log') | ForEach-Object { Join-Path $PSScriptRoot $_ }
foreach ($processDirectory in @('process-final', 'process-final-02')) {
    $runSummary = Read-Json (Join-Path $PSScriptRoot "$processDirectory/summary.json")
    foreach ($record in $runSummary.results) {
        $configuration = $record.configuration.ToLowerInvariant()
        if ($record.scenario -eq 'unknown-error') {
            $all += $record.verification.Diagnostic.DumpPath
            $all += Join-Path $PSScriptRoot "$processDirectory/$configuration/unknown-error/runtime.log"
        }
        else {
            $all += Join-Path $PSScriptRoot "$processDirectory/$configuration/$($record.scenario).runtime.log"
            $all += @($record.backupPaths)
        }
    }
}
$artifacts = @(foreach ($path in @($all | Sort-Object -Unique)) {
    [ordered]@{ path = [IO.Path]::GetRelativePath($PSScriptRoot, $path).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
})
Save-Json $artifacts (Join-Path $PSScriptRoot 'artifact-manifest.json')
$validation | ConvertTo-Json -Depth 18
if (-not $validation.passed) { exit 1 }
