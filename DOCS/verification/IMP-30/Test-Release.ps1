#requires -Version 7.4
param([string]$Case,[string]$OutputDirectory,[string]$CoreAssembly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseTools.ps1')
$null = New-Item -ItemType Directory -Path $OutputDirectory
$null = [Reflection.Assembly]::LoadFrom($CoreAssembly)
if ($Case -eq 'restore-entrypoint') {
    # Publishing may clean Core/bin. Recovery must bootstrap from the attested GUI dependency.
    $mode = if ([SqlXmlAnalyzer.Logger]::IsDebugMode) { 'Debug' } else { 'Release' }
    $repo = Join-Path $OutputDirectory 'isolated checkout'
    $scripts = Join-Path $repo 'DOCS/verification/IMP-30'
    $binaries = Join-Path $repo "bin/$mode/net8.0-windows"
    $null = New-Item -ItemType Directory -Path $scripts,$binaries
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Restore-Bundle.ps1') -Destination $scripts
    Copy-Item -LiteralPath $CoreAssembly -Destination (Join-Path $binaries 'SqlXmlAnalyzer.Core.dll')
    $source = Join-Path $OutputDirectory 'backup'
    $null = New-Item -ItemType Directory -Path $source
    [IO.File]::WriteAllText((Join-Path $source 'original.sql'),'original SQL bytes')
    $hash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Seal($source,'test-backup','recovery')
    $target = Join-Path $OutputDirectory 'restored'
    Invoke-ReleaseProcess pwsh @('-NoProfile','-File',(Join-Path $scripts 'Restore-Bundle.ps1'),'-Bundle',$source,'-ManifestSha256',$hash,'-Destination',$target,'-Configuration',$mode) $repo (Join-Path $OutputDirectory 'restore') | Out-Null
    $null = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Verify($target,$hash)
    if ((Get-Content -LiteralPath (Join-Path $target 'original.sql') -Raw) -cne 'original SQL bytes') { throw 'Recovery entrypoint lost bytes.' }
} elseif ($Case -eq 'git-visible') {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
    foreach ($path in 'SqlXmlAnalyzer.Core/Deployment/ReleaseBundle.cs','SqlXmlAnalyzer.Core/Deployment/ReleaseOperation.cs','DOCS/release-materials/ReleaseNotes.md','DOCS/release-materials/UpgradeRollback.md','DOCS/release-materials/IssueStatus.md') {
        $found = @(git -C $repository ls-files --cached --others --exclude-standard -- $path)
        if ($LASTEXITCODE -ne 0 -or $found.Count -ne 1 -or $found[0] -cne $path) { throw "Release source or document ignored: $path" }
    }
} elseif ($Case -like 'candidate-*') {
    $candidate = Join-Path $OutputDirectory 'candidate'
    $null = New-Item -ItemType Directory -Path (Join-Path $candidate 'gui'),(Join-Path $candidate 'cli')
    [IO.File]::WriteAllText((Join-Path $candidate 'gui/SqlXmlAnalyzer.exe'),'test gui')
    [IO.File]::WriteAllText((Join-Path $candidate 'cli/SqlXmlAnalyzer.CLI.exe'),'test cli')
    $build = Join-Path $candidate 'build-evidence.json'
    [IO.File]::WriteAllText($build,'{"synthetic":"build A"}')
    $buildHash = Get-EvidenceFileHash $build
    $statusHash = if ($Case -eq 'candidate-status-build') { 'B' * 64 } else { $buildHash }
    $id = if ($Case -eq 'candidate-id') { 'wrong-id' } else { 'candidate-A' }
    if ($Case -ne 'candidate-missing-status') { Write-NewEvidenceJson (Join-Path $candidate 'release-status.json') @{CandidateId=$id;BuildSha256=$statusHash;ApprovedForDistribution=$false} }
    if ($Case -eq 'candidate-embedded-build') { [IO.File]::WriteAllText($build,'{"synthetic":"build B"}') }
    $kind = if ($Case -eq 'candidate-kind') { 'recovery' } else { 'candidate' }
    $hash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Seal($candidate,'candidate-A',$kind)
    if ($Case -eq 'candidate-stale-build') { $buildHash = 'A' * 64 }
    $rejected = $false
    try { $identity = Assert-ReleaseCandidate $candidate $hash $buildHash }
    catch [IO.InvalidDataException] { $rejected = $true }
    catch [IO.IOException] { $rejected = $true }
    if ($rejected -ne ($Case -ne 'candidate-match')) { throw 'Candidate/build identity gate returned the wrong result.' }
    if (-not $rejected -and ($identity.GuiSha256 -ne (Get-EvidenceFileHash (Join-Path $candidate 'gui/SqlXmlAnalyzer.exe')) -or $identity.BuildSha256 -ne $buildHash)) { throw 'Candidate identity lost binary/build hashes.' }
} elseif ($Case -like 'expected-*') {
    $logs = Join-Path $OutputDirectory 'diagnostics'
    $code = [SqlXmlAnalyzer.Core.Release.ReleaseOperation]::Run([Action]{
        switch ($Case) {
            'expected-validation' { Assert-EvidenceRecords @() @() }
            'expected-json' { $file=Join-Path $OutputDirectory 'bad.json'; [IO.File]::WriteAllText($file,'{bad'); Read-EvidenceJson $file }
            'expected-missing-file' { Start-AcceptanceStage $OutputDirectory (Join-Path $OutputDirectory 'missing-build.json') }
        }
    },$logs)
    if ($code -ne 1 -or (Test-Path -LiteralPath (Join-Path $logs 'dumps'))) { throw 'Expected validation failure generated a DUMP or returned success.' }
    $log = [IO.File]::ReadAllText((Join-Path $logs 'release.log'))
    if ($log -notmatch '\[ERROR\]' -or $log -match '\[CRITICAL\]') { throw 'Expected failure had the wrong log classification.' }
} elseif ($Case -eq 'boundary' -or $Case -eq 'boundary-unknown-string') {
    $logs = Join-Path $OutputDirectory 'diagnostics'
    $code = [SqlXmlAnalyzer.Core.Release.ReleaseOperation]::Run([Action]{
        [SqlXmlAnalyzer.Logger]::Warning('synthetic warning')
        [SqlXmlAnalyzer.Logger]::Error('synthetic error')
        if ($Case -eq 'boundary-unknown-string') { throw 'Unclassified script failure must still produce a DUMP.' }
        throw [InvalidOperationException]::new('IMP30 PowerShell boundary synthetic failure')
    },$logs)
    if ($code -ne 1) { throw 'Unknown error was incorrectly reported as success.' }
    $dumps = @(Get-ChildItem -LiteralPath (Join-Path $logs 'dumps') -Directory)
    if ($dumps.Count -ne 1) { throw 'Expected one incident.' }
    . (Join-Path $PSScriptRoot '../IMP-29/AcceptanceEvidence.ps1')
    Assert-AcceptanceDump $CoreAssembly (Join-Path $dumps[0].FullName 'process.dmp')
    $log = Get-Content -LiteralPath (Join-Path $logs 'release.log') -Raw
    if ($log -notmatch '\[CRITICAL\]' -or $log -notmatch '\[ERROR\]') { throw 'Required errors missing.' }
    if ([SqlXmlAnalyzer.Logger]::IsDebugMode) { if ($log -notmatch '\[DEBUG\]' -or $log -notmatch '\[WARN\]') { throw 'Debug log missing.' } }
    elseif ($log -match '\[(DEBUG|WARN|INFO|VERBOSE)\]') { throw 'Release log leakage.' }
} elseif ($Case -like 'capture-*') {
    $prefixes = @('candidate-help','candidate-scan','candidate-read','restored-help','restored-scan','restored-read')
    $results = @(Get-ReleaseProcessEvidence $prefixes)
    foreach ($path in $results) { [IO.File]::WriteAllText((Join-Path $OutputDirectory $path),'original evidence') }
    $records = @($results | ForEach-Object { Get-EvidenceHash $OutputDirectory $_ })
    Write-NewEvidenceJson (Join-Path $OutputDirectory 'stage-evidence.json') @{SchemaVersion=1;Passed=$true;BuildSha256=('A' * 64);Results=$records}
    $null = Assert-AcceptanceStage $OutputDirectory ('A' * 64) $results
    $changed = Join-Path $OutputDirectory $(if ($Case -eq 'capture-error-modified') { 'restored-scan.stderr.txt' } else { 'candidate-read.stdout.txt' })
    if ($Case -eq 'capture-read-missing') { Remove-Item -LiteralPath $changed }
    else { [IO.File]::WriteAllText($changed,'changed evidence') }
    $rejected = $false
    try { $null = Assert-AcceptanceStage $OutputDirectory ('A' * 64) $results }
    catch [IO.InvalidDataException] { $rejected = $true }
    catch [IO.IOException] { $rejected = $true }
    if (-not $rejected -or $records.Count -ne 18) { throw 'CLI stdout/stderr was not bound to the stage.' }
} elseif ($Case -eq 'relative-paths') {
    $null = New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'child working directory')
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'session with spaces.json'),'session bytes')
    Push-Location $OutputDirectory
    $oldSession = $env:IMP30_SESSION
    try {
        $env:IMP30_SESSION = Resolve-ReleasePath './session with spaces.json'
        $exe = Resolve-ReleasePath ([IO.Path]::GetRelativePath($OutputDirectory,(Join-Path $PSHOME 'pwsh.exe')))
        $receipt = Invoke-ReleaseProcess $exe @('-NoProfile','-Command','[Console]::WriteLine([IO.File]::ReadAllText($env:IMP30_SESSION)); [Console]::WriteLine([Environment]::ProcessPath)') './child working directory' './relative-process'
        $lines = [IO.File]::ReadAllLines((Join-Path $OutputDirectory 'relative-process.stdout.txt'))
        if ($lines[0] -cne 'session bytes' -or $lines[1] -ne $exe -or $receipt.ExitCode -ne 0) { throw 'Relative session/executable paths changed under child working directory.' }
    }
    finally { $env:IMP30_SESSION = $oldSession; Pop-Location }
} elseif ($Case -eq 'preserve-evidence') {
    $prefix = Join-Path $OutputDirectory 'process'
    [IO.File]::WriteAllText(($prefix + '.stdout.txt'),'retained evidence')
    $rejected = $false
    try { $null = Invoke-ReleaseProcess pwsh @('-NoProfile','-Command','throw "must not launch"') $OutputDirectory $prefix }
    catch [IO.IOException] { $rejected = $true }
    if (-not $rejected -or [IO.File]::ReadAllText(($prefix + '.stdout.txt')) -cne 'retained evidence' -or (Test-Path -LiteralPath ($prefix + '.command.json'))) { throw 'Existing command evidence was overwritten.' }
} elseif ($Case -eq 'pipe-timeout') {
    $oldPidPath = $env:IMP30_CHILD_PID
    $env:IMP30_CHILD_PID = Join-Path $OutputDirectory 'child.json'
    $childCode = '[Console]::WriteLine("child started"); Start-Sleep -Seconds 30'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCode))
    $parent = '$p=[Diagnostics.ProcessStartInfo]::new("pwsh"); $p.UseShellExecute=$false; $p.CreateNoWindow=$true; foreach($a in @("-NoProfile","-EncodedCommand","' + $encoded + '")) { $p.ArgumentList.Add($a) }; $child=[Diagnostics.Process]::Start($p); @{Id=$child.Id;Started=$child.StartTime.ToUniversalTime().Ticks} | ConvertTo-Json | Set-Content -LiteralPath $env:IMP30_CHILD_PID; [Console]::WriteLine("parent completed"); $child.Dispose(); exit 0'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        $rejected = $false
        try { $null = Invoke-ReleaseProcess pwsh @('-NoProfile','-EncodedCommand',[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($parent))) $OutputDirectory (Join-Path $OutputDirectory 'pipe') 2 }
        catch [IO.IOException] { $rejected = $true }
        $receipt = Read-EvidenceJson (Join-Path $OutputDirectory 'pipe.command.json')
        if (-not $rejected -or -not $receipt.TimedOut -or $receipt.ExitCode -ne 0 -or $receipt.StdoutComplete -or $timer.Elapsed.TotalSeconds -gt 6) { throw 'Exited parent with inherited pipe bypassed the deadline.' }
        if ([IO.File]::ReadAllText((Join-Path $OutputDirectory 'pipe.stdout.txt')) -notmatch 'parent completed') { throw 'Timeout discarded already captured output.' }
    }
    finally {
        # The parent has already exited, so Kill(tree) cannot discover this test child.
        # Reap only the exact child identified by PID plus creation time.
        if (Test-Path -LiteralPath $env:IMP30_CHILD_PID) {
            $saved = Read-EvidenceJson $env:IMP30_CHILD_PID
            try {
                $child = [Diagnostics.Process]::GetProcessById($saved.Id)
                try { if ($child.StartTime.ToUniversalTime().Ticks -eq $saved.Started -and -not $child.HasExited) { $child.Kill($true); $null = $child.WaitForExit(1000) } }
                finally { $child.Dispose() }
            }
            catch [ArgumentException] { }
        }
        $env:IMP30_CHILD_PID = $oldPidPath
    }
} else {
    $child = Join-Path $OutputDirectory 'child with spaces.ps1'
    if ($Case -eq 'timeout') { 'Start-Sleep -Seconds 20' | Set-Content -LiteralPath $child }
    elseif ($Case -eq 'failure') { 'exit 7' | Set-Content -LiteralPath $child }
    else { 'param($Value) [Console]::WriteLine($Value); [Console]::Error.WriteLine("stderr captured")' | Set-Content -LiteralPath $child }
    $failed = $false
    try { $null = Invoke-ReleaseProcess 'pwsh' @('-NoProfile','-File',$child,'literal space $value') $OutputDirectory (Join-Path $OutputDirectory 'process') 2 }
    catch [IO.IOException] { $failed = $true }
    if ($failed -ne ($Case -ne 'success')) { throw 'Unexpected process result.' }
    $receipt = Get-Content -LiteralPath (Join-Path $OutputDirectory 'process.command.json') -Raw | ConvertFrom-Json
    if ($Case -eq 'failure' -and $receipt.ExitCode -ne 7) { throw 'Lost nonzero exit code.' }
    if ($Case -eq 'timeout' -and -not $receipt.TimedOut) { throw 'Timeout not captured.' }
    if ($Case -eq 'success' -and (Get-Content -LiteralPath (Join-Path $OutputDirectory 'process.stdout.txt') -Raw).Trim() -cne 'literal space $value') { throw 'Argument quoting changed input.' }
}
Write-Output "PASS: $Case"
