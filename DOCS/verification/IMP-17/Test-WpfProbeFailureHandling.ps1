param(
    [Parameter(Mandatory)][string]$ProbeExecutable,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidateSet('Debug', 'Release')][string]$Configuration
)
$ErrorActionPreference = 'Stop'
$diagnosticsRoot = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP17-probe-regression-' + [Guid]::NewGuid().ToString('N'))
$records = [Collections.Generic.List[object]]::new()
$cases = @('success', 'locked-screenshot', 'existing-screenshot', 'locked-result', 'unknown', 'dump-unavailable', 'locked-log', 'invalid-arguments')
foreach ($case in $cases) {
    $output = Join-Path $EvidenceDirectory $case
    $diagnostics = Join-Path $diagnosticsRoot $case
    [IO.Directory]::CreateDirectory($output) | Out-Null
    [IO.Directory]::CreateDirectory($diagnostics) | Out-Null
    $lock = $null
    $protectedFile = $null
    $sentinel = [Text.Encoding]::UTF8.GetBytes('Existing evidence must remain unchanged.')
    $expectedExit = switch ($case) {
        'success' { 0 }
        'locked-log' { 0 }
        'unknown' { 3 }
        'dump-unavailable' { 3 }
        default { 2 }
    }
    try {
        if ($case -in @('locked-screenshot', 'existing-screenshot', 'locked-result', 'locked-log')) {
            $protectedFile = switch ($case) {
                'locked-result' { Join-Path $output 'wpf-probe.json' }
                'locked-log' { Join-Path $diagnostics 'probe.log' }
                default { Join-Path $output 'comparison-light.png' }
            }
            [IO.File]::WriteAllBytes($protectedFile, $sentinel)
            if ($case -ne 'existing-screenshot') {
                # The parent holds a real exclusive OS file handle for the entire child run.
                $lock = [IO.File]::Open($protectedFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            }
        }
        if ($case -eq 'dump-unavailable') {
            [IO.File]::WriteAllText((Join-Path $diagnostics 'dumps'), 'Blocks creation of the diagnostic directory.')
        }
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = [IO.Path]::GetFullPath($ProbeExecutable)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.StandardOutputEncoding = [Text.Encoding]::UTF8
        $start.StandardErrorEncoding = [Text.Encoding]::UTF8
        if ($case -ne 'invalid-arguments') {
            foreach ($argument in @($Repository, $output, $diagnostics)) { $start.ArgumentList.Add($argument) }
            if ($case -in @('unknown', 'dump-unavailable')) { $start.ArgumentList.Add('--verify-unexpected') }
        }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        try {
            if (!$process.Start()) { throw "Unable to start probe: $case" }
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            if (!$process.WaitForExit(60000)) {
                $process.Kill($true)
                $process.WaitForExit()
                throw "Probe timed out: $case"
            }
            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
            $exitCode = $process.ExitCode
        } finally { $process.Dispose() }
    } finally { if ($null -ne $lock) { $lock.Dispose() } }
    [IO.File]::WriteAllText((Join-Path $output 'stdout.log'), $stdout)
    [IO.File]::WriteAllText((Join-Path $output 'stderr.log'), $stderr)
    if ($exitCode -ne $expectedExit) { throw "$case returned $exitCode; expected $expectedExit. See $output" }
    if ($stderr -match 'Unhandled exception|未处理的异常') { throw "$case leaked an unhandled exception." }
    if ($protectedFile -and [Convert]::ToBase64String([IO.File]::ReadAllBytes($protectedFile)) -ne [Convert]::ToBase64String($sentinel)) {
        throw "$case modified existing evidence."
    }
    if ($expectedExit -eq 0) {
        $result = Get-Content -LiteralPath (Join-Path $output 'wpf-probe.json') -Raw | ConvertFrom-Json
        if (!$result.Passed -or $result.BindingErrors -ne 0 -or !$result.ManualSelectionAndReset -or
            !$result.SameSnapshotSelection -or !$result.ManualSessionRoundtrip -or !$result.OriginalHashRestored -or
            @($result.records).Count -ne 2 -or @($result.records | Where-Object { !$_.ScreenshotCreated }).Count -ne 0 -or $stdout -notmatch 'PASS:') {
            throw "$case did not complete the WPF verification."
        }
    } else {
        if ($stdout -match 'PASS:') { throw "$case incorrectly reported success." }
        if ($stderr -notmatch '\[ERROR\].*FAIL:') { throw "$case did not record a controlled failure." }
        if ($case -ne 'locked-result' -and (Test-Path -LiteralPath (Join-Path $output 'wpf-probe.json'))) {
            throw "$case published a success result after failure."
        }
        if ($expectedExit -eq 2 -and ($stderr -notmatch 'category=Expected' -or $stderr -match '\[CRITICAL\]')) {
            throw "$case incorrectly classified an expected failure."
        }
        if ($expectedExit -eq 3 -and ($stderr -notmatch 'category=Unexpected' -or $stderr -notmatch '\[CRITICAL\]')) {
            throw "$case did not record the fatal diagnostic."
        }
    }
    if ($Configuration -eq 'Release' -and $stderr -match '\[(DEBUG|WARN|INFO)\]') { throw "$case violated Release log filtering." }
    if ($Configuration -eq 'Debug' -and $case -ne 'invalid-arguments' -and $stderr -notmatch '\[DEBUG\]') {
        throw "$case omitted Debug diagnostics."
    }
    $dumps = @()
    $dumpDirectory = Join-Path $diagnostics 'dumps'
    if (Test-Path -LiteralPath $dumpDirectory -PathType Container) {
        # Only inspect this test's bounded incident directory, never enumerate the repository recursively.
        $dumps = @(Get-ChildItem -LiteralPath $dumpDirectory -Directory | ForEach-Object {
            $candidate = Join-Path $_.FullName 'process.dmp'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { Get-Item -LiteralPath $candidate }
        })
    }
    $dumpEvidence = $null
    if ($case -eq 'unknown') {
        if ($dumps.Count -ne 1) { throw 'Unknown failure did not produce exactly one DUMP.' }
        $stream = [IO.File]::OpenRead($dumps[0].FullName)
        try {
            $reader = [IO.BinaryReader]::new($stream)
            if ($stream.Length -le 32 -or $reader.ReadUInt32() -ne 0x504D444D) { throw 'Invalid native minidump header.' }
        } finally { $stream.Dispose() }
        $metadata = Join-Path $dumps[0].DirectoryName 'exception.json'
        if ((Get-Content -LiteralPath $metadata -Raw) -notmatch 'IMP17.WpfProbe') { throw 'DUMP exception sidecar is missing.' }
        $dumpEvidence = @{ Path = $dumps[0].FullName; Length = $dumps[0].Length; SHA256 = (Get-FileHash -LiteralPath $dumps[0].FullName -Algorithm SHA256).Hash }
    } elseif ($dumps.Count -ne 0) { throw "$case unexpectedly generated a DUMP." }
    if ($case -eq 'dump-unavailable' -and $stderr -notmatch 'DUMP 生成失败') { throw 'DUMP failure was not reported.' }
    if ($case -eq 'locked-log' -and $stderr -notmatch '\[ERROR\].*日志写入失败') { throw 'Locked log did not use stderr fallback.' }
    $records.Add([ordered]@{ Case = $case; Passed = $true; ExitCode = $exitCode; ExpectedExitCode = $expectedExit; ProtectedFileUnchanged = ($null -ne $protectedFile); Dump = $dumpEvidence })
    Write-Output "PASS: $Configuration/$case (exit $exitCode)"
}
$summary = [ordered]@{ Configuration = $Configuration; CompletedAt = [DateTimeOffset]::Now.ToString('O'); Passed = $true; Checks = $records.Count; DiagnosticsDirectory = $diagnosticsRoot; Records = $records }
[IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'failure-handling.json'), ($summary | ConvertTo-Json -Depth 8))
