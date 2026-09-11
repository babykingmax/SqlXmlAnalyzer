param([string]$RepositoryPath = 'E:/SqlXmlAnalyzer')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath($RepositoryPath)
$run = Join-Path $repository 'DOCS/acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8'
$previous = Join-Path $repository 'DOCS/acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017'
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'deliverable-validation.json')) { throw 'Immutable evidence already exists. Use a new versioned evidence directory.' }
function Save-Json($value, $path) { ConvertTo-Json -InputObject $value -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8 }
function Read-Json($path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
function Check-Manifest([string]$manifest) {
    $entries = @(Read-Json $manifest)
    $base = Split-Path -Parent $manifest
    $errors = @(foreach ($entry in $entries) {
        $target = Join-Path $base $entry.path
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { "Missing: $target" }
        elseif ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $entry.sha256) { "Changed: $target" }
    })
    [pscustomobject]@{ manifest = [IO.Path]::GetRelativePath($repository, $manifest); entries = $entries.Count; errors = $errors; passed = $errors.Count -eq 0 }
}
$historyManifests = @(
    'DOCS/baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/artifact-manifest.json',
    'DOCS/acceptance/IMP-02/runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/artifact-manifest.json',
    'DOCS/acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/artifact-manifest.json'
)
$manifestChecks = @($historyManifests | ForEach-Object { Check-Manifest (Join-Path $repository $_) })
$manifestChecks += Check-Manifest (Join-Path $run 'artifact-manifest.json')
Import-Module (Join-Path $run 'adapter/AcceptanceInfrastructure.psm1') -Force
$reviewPaths = @('DOCS/review-evidence/ReviewProbe.cs.txt', 'DOCS/review-evidence/Run-ReviewProbe.ps1', 'DOCS/review-evidence/baseline.json', 'DOCS/review-evidence/probe-results.json')
$current = Get-AcceptanceInputSnapshot -RepositoryPath $repository -AdditionalPaths $reviewPaths
$before = Read-Json (Join-Path $run 'input-snapshot-before.json')
$prior = Read-Json (Join-Path $previous 'input-snapshot-before.json')
$finalDrift = @(Compare-AcceptanceInputSnapshot -Before $before -After $current)
$stepChanges = @(Compare-AcceptanceInputSnapshot -Before $prior -After $current)
$ownedPaths = @(
    'App.xaml.cs', 'Core/Services/PlanAnalysisService.cs', 'SqlXmlAnalyzer.CLI/Program.cs',
    'SqlXmlAnalyzer.Tests/Application/ApplicationOrchestratorTests.cs', 'SqlXmlAnalyzer.Tests/Application/CliProgramTests.cs',
    'SqlXmlAnalyzer.Tests/Application/SqlWritebackServiceTests.cs',
    'src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs', 'src/SqlXmlAnalyzer.Application/Models/OrchestratorResult.cs',
    'src/SqlXmlAnalyzer.Application/Models/SqlFileSnapshot.cs', 'src/SqlXmlAnalyzer.Application/Models/SqlWritebackResult.cs',
    'src/SqlXmlAnalyzer.Application/Services/ConsoleResultReporter.cs', 'src/SqlXmlAnalyzer.Application/Services/ISqlWritebackService.cs',
    'src/SqlXmlAnalyzer.Application/Services/PhysicalSqlWritebackFileSystem.cs', 'src/SqlXmlAnalyzer.Application/Services/SqlReportPathGuard.cs',
    'src/SqlXmlAnalyzer.Application/Services/SqlWritebackService.cs'
)
$unexpectedChanges = @($stepChanges | Where-Object { $_.path -notin $ownedPaths })
$userPaths = @('AGENTS.md', 'MainWindow.Services.cs', 'Views/ShellNavigationRail.xaml', 'MainWindow.KeyboardShortcuts.cs')
$retained = @(foreach ($path in @($userPaths + $reviewPaths)) {
    $old = @($prior.inputs | Where-Object path -CEQ $path)
    $now = @($current.inputs | Where-Object path -CEQ $path)
    [pscustomobject]@{ path = $path; sha256Before = $old[0].sha256; sha256After = $now[0].sha256; unchanged = $old.Count -eq 1 -and $now.Count -eq 1 -and $old[0].sha256 -ceq $now[0].sha256 }
})
$docs = @('DOCS/IMP-03安全写回与恢复说明.md', 'DOCS/软件改善实施规划.md', 'DOCS/IMP-02审查反例验收矩阵.md', 'DOCS/acceptance/IMP-02/README.md')
$adapter = @('DOCS/acceptance/IMP-02/AcceptanceProbe.cs.txt', 'DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1', 'DOCS/acceptance/IMP-02/acceptance-matrix.json')
$ownership = [ordered]@{ head = $current.head; comparisonBase = [IO.Path]::GetRelativePath($repository, $previous); implementation = $stepChanges; preservedPreexistingChanges = @($retained | Where-Object { $_.path -in $userPaths }); currentAdapterChanges = $adapter; documentChanges = $docs; unexpectedChanges = $unexpectedChanges; committed = $false }
Save-Json $ownership (Join-Path $PSScriptRoot 'change-ownership.json')
Save-Json $current (Join-Path $PSScriptRoot 'input-snapshot-final.json')
$snapshotEntries = @(foreach ($path in @($ownedPaths + $adapter + $docs)) {
    $destination = Join-Path $PSScriptRoot ('source-snapshots/' + $path + '.snapshot')
    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    [IO.File]::Copy((Join-Path $repository $path), $destination, $false)
    [ordered]@{ source = $path; snapshot = [IO.Path]::GetRelativePath($PSScriptRoot, $destination); sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
})
Save-Json $snapshotEntries (Join-Path $PSScriptRoot 'source-snapshot-manifest.json')
$inventory = @(git -C $repository -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed' }
$inventory | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'git-ls-files.txt') -Encoding utf8
git -C $repository -c core.quotepath=false status --short 1> (Join-Path $PSScriptRoot 'git-status-final.txt')
if ($LASTEXITCODE -ne 0) { throw 'git status failed' }
git -C $repository diff --check -- @ownedPaths 1> (Join-Path $PSScriptRoot 'diff-check.stdout.txt') 2> (Join-Path $PSScriptRoot 'diff-check.stderr.txt')
$diffExit = $LASTEXITCODE
git -C $repository diff --no-ext-diff -- @ownedPaths 1> (Join-Path $PSScriptRoot 'tracked-implementation.diff') 2> (Join-Path $PSScriptRoot 'tracked-diff.stderr.txt')
if ($LASTEXITCODE -ne 0) { throw 'git diff failed' }
$summary = Read-Json (Join-Path $run 'summary.json')
[xml]$trx = Get-Content -LiteralPath (Join-Path $run 'test-output/existing-suite.trx') -Raw
$counts = $trx.TestRun.ResultSummary.Counters
$smoke = Read-Json (Join-Path $PSScriptRoot 'cli-smoke-results.json')
$r03 = @(Read-Json (Join-Path $run 'acceptance-results.json') | Where-Object reviewId -EQ 'R03')[0]
$development = @(foreach ($number in 1..5) {
    $stem = 'writeback-tests-{0:D2}' -f $number
    $trxPath = Join-Path $PSScriptRoot "$stem.trx"
    $logPath = Join-Path $PSScriptRoot "$stem.log"
    $counters = $null
    if (Test-Path -LiteralPath $trxPath) { [xml]$attempt = Get-Content -LiteralPath $trxPath -Raw; $counters = $attempt.TestRun.ResultSummary.Counters }
    [ordered]@{ attempt = $stem; category = 'Current implementation development, not historical baseline failure'; logExists = Test-Path -LiteralPath $logPath; trxExists = Test-Path -LiteralPath $trxPath; passed = $(if ($counters) { [int]$counters.passed } else { $null }); failed = $(if ($counters) { [int]$counters.failed } else { $null }) }
})
$validation = [ordered]@{
    timestampUtc = [DateTime]::UtcNow.ToString('o'); step = 'IMP-03'; head = $current.head; finalAcceptanceRun = $summary.runId;
    historyAndFinalArtifactChecks = $manifestChecks; retainedInputs = $retained; protectedInputDriftSinceFinalRun = $finalDrift;
    unexpectedChangesSinceImp02 = $unexpectedChanges; ownedSourceFiles = $ownedPaths.Count; snapshottedSourcesAdapterAndDocuments = $snapshotEntries.Count;
    build = [ordered]@{ exitCode = 0; warnings = 0; errors = 0; log = [IO.Path]::GetRelativePath($repository, (Join-Path $run 'build.stdout.txt')) };
    testSuite = [ordered]@{ total = [int]$counts.total; passed = [int]$counts.passed; failed = [int]$counts.failed; notExecuted = [int]$counts.notExecuted };
    infrastructureTests = $summary.infrastructureTests; cliSmoke = [ordered]@{ total = $smoke.total; passed = $smoke.passed };
    r03 = $r03; remainingNotMet = $summary.notMet; issuesAutomaticallyClosed = 0; developmentAttempts = $development; diffCheckExitCode = $diffExit;
    sqlServerExecutionPerformed = $false; wpfVisualTestPerformed = $false; coverageGenerated = $false;
    passed = @($manifestChecks | Where-Object { -not $_.passed }).Count -eq 0 -and @($retained | Where-Object { -not $_.unchanged }).Count -eq 0 -and $finalDrift.Count -eq 0 -and $unexpectedChanges.Count -eq 0 -and [int]$counts.passed -eq 852 -and [int]$counts.failed -eq 0 -and $summary.infrastructureTests.passed -eq 32 -and $smoke.passed -eq 3 -and $r03.status -eq 'ProbeConditionMet' -and $diffExit -eq 0
}
Save-Json $validation (Join-Path $PSScriptRoot 'deliverable-validation.json')
# Inventory only this versioned evidence directory. Include known ignored logs/backups explicitly.
$relativeEvidence = [IO.Path]::GetRelativePath($repository, $PSScriptRoot).Replace('\', '/')
$paths = @(git -C $repository -c core.quotepath=false ls-files --others --exclude-standard -- $relativeEvidence)
if ($LASTEXITCODE -ne 0) { throw 'Evidence inventory failed' }
$absolutePaths = @($paths | ForEach-Object { Join-Path $repository $_ })
$absolutePaths += @(1..5 | ForEach-Object { Join-Path $PSScriptRoot ('writeback-tests-{0:D2}.log' -f $_) } | Where-Object { Test-Path -LiteralPath $_ })
$absolutePaths += @($smoke.results | ForEach-Object { $_.backups } | ForEach-Object { $_.path })
$entries = @(foreach ($path in @($absolutePaths | Sort-Object -Unique)) { [ordered]@{ path = [IO.Path]::GetRelativePath($PSScriptRoot, $path).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } })
Save-Json $entries (Join-Path $PSScriptRoot 'artifact-manifest.json')
$validation | ConvertTo-Json -Depth 16
if (-not $validation.passed) { exit 1 }
