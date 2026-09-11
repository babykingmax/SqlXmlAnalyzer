#requires -Version 7.0
param([switch]$VerifySources)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$selection = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'accepted-runs.json') -Raw | ConvertFrom-Json
$root = $selection.Root
$buildEvidencePath = Join-Path $repository "$root/$($selection.BuildTests)/build-evidence.json"
# Never regenerate execution-time identity from today's checkout or binaries.
$acceptedBuild = Read-AcceptedBuild $repository $buildEvidencePath
$buildSha256 = (Get-FileHash -LiteralPath $buildEvidencePath).Hash
function Hash([string]$relative) {
    Get-EvidenceHash $repository $relative
}
if ($VerifySources) {
    foreach ($manifest in 'sources.json','artifacts.json') {
        $records = Get-Content -LiteralPath (Join-Path $PSScriptRoot $manifest) -Raw | ConvertFrom-Json
        if ($records.Count -lt 100) { throw "Incomplete manifest: $manifest" }
        foreach ($record in $records) {
            if ((Hash $record.Path).Sha256 -ne $record.Sha256) { throw "Changed evidence: $($record.Path)" }
        }
        Write-Output "Verified $($records.Count) hashes in $manifest"
    }
    return
}
$artifacts = [Collections.Generic.List[object]]::new()
$copies = [Collections.Generic.List[object]]::new()
function Record([string]$relative) { $artifacts.Add((Hash $relative)) }
function ReadJson([string]$relative) { Record $relative; Get-Content -LiteralPath (Join-Path $repository $relative) -Raw | ConvertFrom-Json }
function Passed($result,[string]$name) { if ($result.Passed -isnot [bool] -or -not $result.Passed) { throw "Not accepted: $name" } }
function CopyEvidence([string]$relative,[string]$name) {
    Record $relative
    $copies.Add(@{Source=$relative; Name=$name})
}
function Bound([string]$relative,[string[]]$required) {
    $stage = Assert-AcceptanceStage (Join-Path $repository $relative) $buildSha256 $required
    Record "$relative/stage-evidence.json"
    foreach ($result in $stage.Results) { Record "$relative/$($result.Path)" }
}
CopyEvidence "$root/$($selection.BuildTests)/build-evidence.json" 'build-evidence.json'
$modes = foreach ($configuration in $selection.Configurations) {
    $lower = $configuration.ToLowerInvariant()
    $build = "$root/$($selection.BuildTests)/$configuration"
    $mode = @($acceptedBuild.Modes | Where-Object Configuration -eq $configuration)
    if ($mode.Count -ne 1) { throw 'Missing configuration in build attestation.' }
    Assert-StrictBuild $mode[0].Build
    $buildReceipt = ReadJson "$build/build.log.json"; Assert-StrictBuild $buildReceipt
    Record "$build/build.log"; Record "$build/test.log"
    $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = 'Prohibit'; $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create((Join-Path $repository "$build/full.trx"),$settings)
    $names = [Collections.Generic.List[string]]::new(); $counters = $null
    try {
        while ($reader.Read()) {
            if ($reader.NodeType -ne 'Element') { continue }
            if ($reader.LocalName -eq 'Counters') {
                $counters = @{Total=[int]$reader.GetAttribute('total'); Passed=[int]$reader.GetAttribute('passed'); Failed=[int]$reader.GetAttribute('failed'); NotExecuted=[int]$reader.GetAttribute('notExecuted')}
            }
            if ($reader.LocalName -eq 'UnitTestResult') {
                if ($reader.GetAttribute('outcome') -ne 'Passed') { throw 'TRX contains a non-passing test.' }
                $names.Add($reader.GetAttribute('testName'))
            }
        }
    } finally { $reader.Dispose() }
    if ($null -eq $counters -or $counters.Total -ne $selection.ExpectedTests -or $counters.Passed -ne $counters.Total -or $counters.Failed -ne 0 -or $counters.NotExecuted -ne 0 -or $names.Count -ne $counters.Total) { throw 'Incomplete TRX.' }
    Record "$build/full.trx"
    $newTests = @($names | Where-Object { $_ -match 'AcceptanceUserScenarioTests|AcceptanceProbeBoundaryTests|ReportReviewLayoutAcceptanceTests' })
    if ($newTests.Count -ne 12) { throw 'Missing IMP29 scenario/boundary/layout regression tests.' }
    $hardeningTests = @($names | Where-Object { $_ -match 'AcceptanceEvidenceScriptTests' })
    if ($hardeningTests.Count -ne $selection.HardeningTests) { throw 'Missing acceptance script regressions.' }
    $diagnosticTests = @($names | Where-Object { $_ -match 'DiagnosticLoggingTests|CompatibilityDiagnosticTests|RuleConfigurationDiagnosticTests|AcceptanceProbeBoundaryTests' })
    $wpf = foreach ($probe in $selection.WpfProbes) {
        $suffix = if ($probe -eq '24') { $selection.Wpf24Suffix } else { '' }
        $directory = "$root/wpf-$lower-$probe$suffix"
        Bound $directory @('boundary.json','run.json','verification.json','build.log.json')
        Passed (ReadJson "$directory/boundary.json") "$configuration WPF $probe boundary"
        $run = ReadJson "$directory/run.json"; Passed $run "$configuration WPF $probe run"
        $reused = 'DOCS/verification/' + $run.Source
        if ((Hash $reused).Sha256 -ne $run.SourceSha256 -or (Hash "$directory/host/Program.cs").Sha256 -ne $run.GeneratedSourceSha256 -or
            (Hash "$directory/host/AcceptanceProbeBoundary.cs").Sha256 -ne (Hash 'SqlXmlAnalyzer.Tests/AcceptanceProbeBoundary.cs').Sha256) { throw 'Probe source bytes changed after execution.' }
        Record $reused; Record "$directory/host/Program.cs"; Record "$directory/host/AcceptanceProbeBoundary.cs"
        $verification = ReadJson "$directory/verification.json"
        if ($null -ne $verification.Passed) { Passed $verification "$configuration WPF $probe" }
        if ($null -ne $verification.BindingErrors -and $verification.BindingErrors -ne 0 -and $verification.BindingErrors -ne '') { throw 'Binding errors in accepted WPF record.' }
        CopyEvidence "$directory/verification.json" "$lower/wpf-$probe.json"
        CopyEvidence "$directory/stage-evidence.json" "$lower/wpf-$probe-attestation.json"
        Record "$directory/build.log"
        [ordered]@{Probe=$probe; Passed=$true; Checks=@($verification.Checks).Count; Evidence="evidence/$lower/wpf-$probe.json"}
    }
    $database = "$root/database-$lower"
    Bound $database @('runs.json','semantics/summary.json','observer/summary.json','types/summary.json','apply/summary.json')
    CopyEvidence "$database/stage-evidence.json" "$lower/sql-attestation.json"
    $suites = ReadJson "$database/runs.json"
    if ($suites.Count -ne 4) { throw 'Incomplete SQL suites.' }
    foreach ($suite in $suites) { Passed $suite $suite.Suite }
    $sqlCount = 0
    foreach ($suite in 'semantics','observer','types') {
        $results = ReadJson "$database/$suite/summary.json"
        foreach ($result in $results) { Passed $result "$configuration $suite" }
        $sqlCount += $results.Count
        CopyEvidence "$database/$suite/summary.json" "$lower/sql-$suite.json"
    }
    if ($sqlCount -ne 100) { throw 'Missing SQL semantic cases.' }
    $apply = ReadJson "$database/apply/summary.json"; Passed $apply "$configuration apply"
    if (-not $apply.BackupVerified -or -not $apply.ReportFailureRetainsCommit) { throw 'Apply evidence incomplete.' }
    CopyEvidence "$database/apply/summary.json" "$lower/sql-apply.json"
    foreach ($suite in 'semantics','observer','types','apply') {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repository "$database/$suite") -File) { Record "$database/$suite/$($file.Name)" }
    }
    $indexCount = 0
    foreach ($version in '15.0','17.0') {
        Bound "$root/index-$lower-$version" @('localdb-results.json')
        CopyEvidence "$root/index-$lower-$version/stage-evidence.json" "$lower/index-$version-attestation.json"
        $index = ReadJson "$root/index-$lower-$version/localdb-results.json"; Passed $index "$configuration index $version"
        foreach ($check in $index.Checks) { if (-not $check.TargetAndColumnOrdinalsVerified -or -not $check.RollbackVerified -or -not $check.WrongServerRejected) { throw 'Index target evidence incomplete.' } }
        $indexCount += $index.Checks.Count
        Record "$root/index-$lower-$version/replay.log"
        CopyEvidence "$root/index-$lower-$version/localdb-results.json" "$lower/index-$version.json"
    }
    if ($indexCount -ne 10) { throw 'Missing index targets.' }
    Bound "$root/compatibility-$lower" @("latest-$configuration.json")
    CopyEvidence "$root/compatibility-$lower/stage-evidence.json" "$lower/compatibility-attestation.json"
    $compatibility = ReadJson "$root/compatibility-$lower/latest-$configuration.json"; Passed $compatibility "$configuration compatibility"
    if ($compatibility.CliSha256 -ne (Hash "SqlXmlAnalyzer.CLI/bin/$configuration/net8.0/SqlXmlAnalyzer.CLI.dll").Sha256 -or
        $compatibility.AppSha256 -ne (Hash "bin/$configuration/net8.0-windows/SqlXmlAnalyzer.dll").Sha256 -or
        $compatibility.CoreSha256 -ne (Hash "bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Core.dll").Sha256) { throw 'Compatibility ran against different assemblies.' }
    if ($compatibility.Checks.Count -ne 22) { throw 'Missing compatibility checks.' }
    foreach ($file in $compatibility.Artifacts) {
        $relative = "$root/compatibility-$lower/$($compatibility.Run)/$($file.Path)"
        if ((Hash $relative).Sha256 -ne $file.Sha256) { throw 'CLI compatibility artifact changed.' }
        Record $relative
    }
    CopyEvidence "$root/compatibility-$lower/latest-$configuration.json" "$lower/compatibility.json"
    Bound "$root/cli-failures-$lower" @('verification.json')
    CopyEvidence "$root/cli-failures-$lower/stage-evidence.json" "$lower/cli-invalid-attestation.json"
    $invalid = ReadJson "$root/cli-failures-$lower/verification.json"
    if ($invalid.Count -ne 3) { throw 'Missing invalid-input CLI checks.' }
    foreach ($case in $invalid) { Passed $case "$configuration invalid CLI" }
    CopyEvidence "$root/cli-failures-$lower/verification.json" "$lower/cli-invalid.json"
    Bound "$root/xel-$lower" @('verification.json','boundary.json','run.json')
    CopyEvidence "$root/xel-$lower/stage-evidence.json" "$lower/xel-attestation.json"
    $xel = ReadJson "$root/xel-$lower/verification.json"; Passed $xel "$configuration XEL"
    if ($xel.Events -ne 2 -or $xel.Groups -ne 1 -or $xel.CorruptInputStatus -ne 'Invalid') { throw 'XEL evidence incomplete.' }
    CopyEvidence "$root/xel-$lower/verification.json" "$lower/xel.json"
    Bound "$root/diagnostics-$lower" @('verification.json','boundary.json','run.json')
    $diagnostic = ReadJson "$root/diagnostics-$lower/verification.json"; Passed $diagnostic "$configuration diagnostics"
    if (-not $diagnostic.ExpectedFailureObserved -or $diagnostic.ValidatedNativeDumps -ne 1 -or -not $diagnostic.LogPolicyVerified) { throw 'Diagnostic evidence incomplete.' }
    CopyEvidence "$root/diagnostics-$lower/verification.json" "$lower/diagnostics.json"
    CopyEvidence "$root/diagnostics-$lower/stage-evidence.json" "$lower/diagnostics-attestation.json"
    foreach ($binary in $mode[0].Binaries) { Record $binary.Path }
    [ordered]@{Configuration=$configuration; BuildWarnings=0; BuildErrors=0; Tests=$counters; AddedTests=$newTests; HardeningTests=$hardeningTests; DiagnosticTests=$diagnosticTests; Diagnostics=$diagnostic; Wpf=$wpf; SqlChecks=$sqlCount; IndexTargets=$indexCount; CliCompatibilityChecks=$compatibility.Checks.Count; CliInvalidChecks=$invalid.Count; Xel=$xel}
}
$performance = foreach ($input in $selection.PerformanceInputs) {
    $directory = "$root/$($selection.PerformancePrefix)$input"
    Bound $directory @('verification.json','boundary.json','run.json')
    Passed (ReadJson "$directory/boundary.json") "Performance $input boundary"
    Passed (ReadJson "$directory/run.json") "Performance $input run"
    $result = ReadJson "$directory/verification.json"; Passed $result "Performance $input"
    if ($result.Runs.Count -ne 2) { throw 'Incomplete cold/warm evidence.' }
    CopyEvidence "$directory/verification.json" "performance/$input.json"
    CopyEvidence "$directory/stage-evidence.json" "performance/$input-attestation.json"
    [ordered]@{Input=$input; SourceSha256=$result.SourceSha256; Runs=@($result.Runs | Select-Object Run,TotalMs,InputLatencyP95Ms,InputLatencyMaxMs,Samples,PeakWorkingSetBytes,VisibleGraphNodes,Operators,XelEvents,State)}
}
Record "$root/hardware.json"; CopyEvidence "$root/hardware.json" 'performance/hardware.json'
Bound "$root/xel-input" @('xel-fixture.json','multiple.xel')
CopyEvidence "$root/xel-input/xel-fixture.json" 'xel-fixture.json'
CopyEvidence "$root/xel-input/stage-evidence.json" 'xel-attestation.json'
Record "$root/$($selection.BuildTests)/restore.log"
$fixtures = Get-Content -LiteralPath (Join-Path $repository 'DOCS/verification/IMP-28/fixture-hashes.json') -Raw | ConvertFrom-Json
foreach ($fixture in $fixtures) { if ((Hash $fixture.Path).Sha256 -ne $fixture.Sha256) { throw "Frozen fixture changed: $($fixture.Path)" } }
$sources = @($acceptedBuild.Sources)
$documentation = @('DOCS/verification/IMP-29/accepted-runs.json','DOCS/verification/IMP-29/README.md','DOCS/verification/IMP-29/acceptance-matrix.md','DOCS/IMP-29完整构建测试与用户场景验收.md','DOCS/IMP-29审查修复与加固说明.md','README.md','CHANGELOG.md','Architecture.md','UserGuide.md','DOCS/README.md','DOCS/软件改善实施规划.md')
$sources += @($documentation | ForEach-Object { Hash $_ })
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'desktop') -File) { Record ('DOCS/verification/IMP-29/desktop/' + $file.Name) }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'screenshots') -File) { Record ('DOCS/verification/IMP-29/screenshots/' + $file.Name) }
$null = Read-AcceptedBuild $repository $buildEvidencePath
foreach ($copy in $copies) {
    $target = Join-Path $PSScriptRoot ('evidence/' + $copy.Name)
    $null = New-Item -ItemType Directory -Path (Split-Path $target) -Force
    Copy-Item -LiteralPath (Join-Path $repository $copy.Source) -Destination $target
    Record ('DOCS/verification/IMP-29/evidence/' + $copy.Name)
}
$sources = @($sources | Sort-Object -Property Path -Unique)
$sources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'sources.json') -Encoding utf8
[ordered]@{ Implementation='IMP-29'; Revision='Review hardening'; GeneratedAtUtc=[DateTime]::UtcNow.ToString('O'); GitHead=(git -C $repository rev-parse HEAD); WorkingTree='Uncommitted cumulative IMP23-29 work'; BaselineTests=2754; AddedTests=(12 + $selection.HardeningTests); HardeningTests=$selection.HardeningTests; BuildEvidenceSha256=$buildSha256; Configurations=@($modes); Performance=@($performance); SourceCount=$sources.Count; FrozenFixturesUnchanged=$fixtures.Count; CoverageGenerated=$false; AutomatedAcceptancePassed=$true; OverallAcceptance='Conditional: see acceptance-matrix.md for unaccepted physical DPI, assistive technology, external viewers and performance goals'; ReleaseGate='NotGranted; IMP30 is separate'; ValidatedNativeDumps=2 } |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8
Record 'DOCS/verification/IMP-29/summary.json'
$uniqueArtifacts = @($artifacts | Sort-Object -Property { $_.Path } -Unique)
if ($uniqueArtifacts.Count -lt 100) { throw 'Artifact manifest lost required records.' }
ConvertTo-Json -InputObject $uniqueArtifacts -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts.json') -Encoding utf8
Write-Output 'Wrote current IMP29 validation summary, source and artifact manifests.'
