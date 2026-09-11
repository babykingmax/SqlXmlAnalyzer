#requires -Version 7.4
. (Join-Path $PSScriptRoot '../IMP-29/AcceptanceEvidence.ps1')

function Resolve-ReleasePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw [IO.InvalidDataException]::new('Release path is empty.') }
    try { [IO.Path]::GetFullPath($Path, (Get-Location).ProviderPath) }
    catch [ArgumentException] { throw [IO.InvalidDataException]::new('Invalid release filesystem path.', $_.Exception) }
}

function Assert-ReleaseCandidate([string]$Candidate, [string]$ManifestSha256, [string]$BuildSha256) {
    $candidatePath = Resolve-ReleasePath $Candidate
    $manifest = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Verify($candidatePath,$ManifestSha256)
    $status = Read-EvidenceJson (Join-Path $candidatePath 'release-status.json')
    $embeddedHash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash((Join-Path $candidatePath 'build-evidence.json'))
    if ($manifest.Kind -cne 'candidate' -or $status.CandidateId -cne $manifest.Id -or
        $BuildSha256 -notmatch '\A[0-9A-Fa-f]{64}\z' -or $embeddedHash -ne $BuildSha256 -or $status.BuildSha256 -ne $BuildSha256 -or
        $status.ApprovedForDistribution -isnot [bool] -or $status.ApprovedForDistribution) {
        throw [IO.InvalidDataException]::new('Candidate does not belong to the accepted build.')
    }
    [pscustomobject]@{
        Directory=$candidatePath; CandidateId=$manifest.Id; ManifestSha256=$ManifestSha256; BuildSha256=$embeddedHash
        GuiSha256=[SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash((Join-Path $candidatePath 'gui/SqlXmlAnalyzer.exe'))
        CliSha256=[SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash((Join-Path $candidatePath 'cli/SqlXmlAnalyzer.CLI.exe'))
    }
}

function Get-ReleaseProcessEvidence([string[]]$Prefixes) {
    foreach ($prefix in $Prefixes) { foreach ($suffix in 'command.json','stdout.txt','stderr.txt') { "$prefix.$suffix" } }
}

function Stop-ReleaseProcess([Diagnostics.Process]$Process) {
    try {
        if (-not $Process.HasExited) { $Process.Kill($true) }
        if (-not $Process.WaitForExit(1000)) { throw [IO.IOException]::new('Release process did not exit during bounded cleanup.') }
    }
    catch {
        # Cleanup must not replace the original command failure, including unknown errors.
        $null = [SqlXmlAnalyzer.Core.Diagnostics.ExceptionPolicy]::Describe($_.Exception,'IMP30.ProcessCleanup',$null)
    }
}

function Invoke-ReleaseProcess([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [string]$EvidencePrefix, [int]$TimeoutSeconds = 120) {
    if ($TimeoutSeconds -le 0 -or $TimeoutSeconds -gt 86400) { throw [IO.InvalidDataException]::new('Invalid release process timeout.') }
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.WorkingDirectory = Resolve-ReleasePath $WorkingDirectory
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $prefix = Resolve-ReleasePath $EvidencePrefix
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    $started = $false; $stdoutFile = $null; $stderrFile = $null
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        # Reserve evidence paths before launch; never overwrite previous evidence.
        if ([IO.Path]::Exists($prefix + '.command.json')) { throw [IO.IOException]::new('Release command evidence already exists.') }
        $stdoutFile = [IO.File]::Open($prefix + '.stdout.txt',[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        $stderrFile = [IO.File]::Open($prefix + '.stderr.txt',[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        try { $started = $process.Start() }
        catch [ComponentModel.Win32Exception] { throw [IO.IOException]::new('Cannot start release process.', $_.Exception) }
        if (-not $started) { throw [IO.IOException]::new('Process did not start.') }
        # Stream to disk. One deadline covers exit AND both pipes, including inherited handles.
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile,$deadline.Token)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($stderrFile,$deadline.Token)
        $exit = $process.WaitForExitAsync($deadline.Token)
        $timedOut = $false
        try { [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr,$exit)).WaitAsync($deadline.Token).GetAwaiter().GetResult() }
        catch [OperationCanceledException] { $timedOut = $true; Stop-ReleaseProcess $process }
        if ($timedOut) {
            $deadline.Cancel()
            $process.StandardOutput.Dispose(); $process.StandardError.Dispose()
            try { [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr)).WaitAsync([TimeSpan]::FromSeconds(1)).GetAwaiter().GetResult() }
            catch [OperationCanceledException] { }
            catch [TimeoutException] { }
            catch [ObjectDisposedException] { }
            catch [IO.IOException] { }
        }
        $stdoutComplete = $stdout.IsCompletedSuccessfully; $stderrComplete = $stderr.IsCompletedSuccessfully
        $stdoutFile.Dispose(); $stdoutFile = $null; $stderrFile.Dispose(); $stderrFile = $null
        $receipt = [ordered]@{
            Executable=$Executable; Arguments=$Arguments; ExitCode=$(if ($process.HasExited) { $process.ExitCode } else { $null })
            TimedOut=$timedOut; StdoutComplete=$stdoutComplete; StderrComplete=$stderrComplete
            ElapsedMilliseconds=$timer.ElapsedMilliseconds; CompletedAtUtc=[DateTime]::UtcNow.ToString('O')
        }
        Write-NewEvidenceJson ($prefix + '.command.json') $receipt
        if ($timedOut -or -not $stdoutComplete -or -not $stderrComplete -or $receipt.ExitCode -ne 0) {
            throw [IO.IOException]::new("Release command failed; see $prefix.command.json")
        }
        $receipt
    }
    finally {
        $deadline.Cancel()
        if ($started) { Stop-ReleaseProcess $process }
        $process.Dispose()
        if ($null -ne $stdoutFile) { $stdoutFile.Dispose() }
        if ($null -ne $stderrFile) { $stderrFile.Dispose() }
        $deadline.Dispose()
    }
}
