# Shared by the runner and its isolated regression checks. No commands run on import.
Set-StrictMode -Version Latest

function Get-AcceptanceInputSnapshot {
    param([Parameter(Mandatory)][string]$RepositoryPath, [string[]]$AdditionalPaths = @())

    # Git is the inventory provider; never recursively enumerate the repository.
    $tracked = @(git -C $RepositoryPath -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot obtain tracked input inventory.' }
    $untracked = @(git -C $RepositoryPath -c core.quotepath=false ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot obtain untracked input inventory.' }
    $head = git -C $RepositoryPath rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot obtain input HEAD.' }
    $paths = @(@($tracked + $untracked | Where-Object {
        $_ -notlike 'DOCS/*' -and
        $_ -notmatch '(?i)(^|/)(\.git|bin|obj|\.vs|publish(?:-[^/]*)?|backups|\.tmp\.[^/]*)(/|$)'
    }) + $AdditionalPaths | Sort-Object -Unique)
    $inputs = @(foreach ($path in $paths) {
        $absolute = Join-Path $RepositoryPath $path
        $exists = Test-Path -LiteralPath $absolute -PathType Leaf
        [pscustomobject]@{
            path = $path
            exists = $exists
            sha256 = $(if ($exists) { (Get-FileHash -LiteralPath $absolute -Algorithm SHA256 -ErrorAction Stop).Hash } else { $null })
        }
    })
    [pscustomobject]@{ head = $head.Trim(); inputs = $inputs }
}

function Compare-AcceptanceInputSnapshot {
    param([Parameter(Mandatory)]$Before, [Parameter(Mandatory)]$After)

    if ($Before.head -cne $After.head) {
        [pscustomobject]@{ kind = 'HeadChanged'; path = 'HEAD'; before = $Before.head; after = $After.head }
    }
    $old = @{}; $new = @{}
    foreach ($inputFile in $Before.inputs) { $old[$inputFile.path] = $inputFile }
    foreach ($inputFile in $After.inputs) { $new[$inputFile.path] = $inputFile }
    foreach ($path in @(@($old.Keys) + @($new.Keys) | Sort-Object -Unique)) {
        $kind = $null
        if (-not $old.ContainsKey($path)) { $kind = 'Added' }
        elseif (-not $new.ContainsKey($path)) { $kind = 'Removed' }
        elseif ($old[$path].exists -and -not $new[$path].exists) { $kind = 'Deleted' }
        elseif (-not $old[$path].exists -and $new[$path].exists) { $kind = 'Restored' }
        elseif ($old[$path].sha256 -cne $new[$path].sha256) { $kind = 'ContentChanged' }
        if ($null -ne $kind) {
            [pscustomobject]@{
                kind = $kind; path = $path
                before = $(if ($old.ContainsKey($path)) { $old[$path].sha256 } else { $null })
                after = $(if ($new.ContainsKey($path)) { $new[$path].sha256 } else { $null })
            }
        }
    }
}

function Copy-FrozenAcceptanceInput {
    param(
        [Parameter(Mandatory)][string]$SnapshotPath,
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][string]$ExpectedHash
    )
    if ((Get-FileHash -LiteralPath $SnapshotPath -Algorithm SHA256 -ErrorAction Stop).Hash -cne $ExpectedHash) {
        throw "Frozen input changed before use: $SnapshotPath"
    }
    [IO.File]::Copy($SnapshotPath, $DestinationPath, $false)
    if ((Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256 -ErrorAction Stop).Hash -cne $ExpectedHash -or
        (Get-FileHash -LiteralPath $SnapshotPath -Algorithm SHA256 -ErrorAction Stop).Hash -cne $ExpectedHash) {
        throw "Frozen input changed while copying: $SnapshotPath"
    }
}

function Get-RefactorProbeCheck {
    param([Parameter(Mandatory)][ValidateSet('R02','R19','R20')][string]$ReviewId, [Parameter(Mandatory)]$Observation)

    foreach ($name in @('IsSuccess','OutputSql','Errors','parseErrorCount','ruleFailures','changeCount','warnings','safetySkips')) {
        if ($null -eq $Observation.PSObject.Properties[$name]) { throw "$ReviewId observation is missing $name." }
    }
    if ($Observation.IsSuccess -isnot [bool] -or $Observation.OutputSql -isnot [string] -or
        ($Observation.parseErrorCount -isnot [int] -and $Observation.parseErrorCount -isnot [long]) -or $Observation.parseErrorCount -lt 0 -or
        ($Observation.changeCount -isnot [int] -and $Observation.changeCount -isnot [long]) -or $Observation.changeCount -lt 0 -or
        $Observation.Errors -isnot [array] -or $Observation.ruleFailures -isnot [array] -or
        $Observation.warnings -isnot [array] -or $Observation.safetySkips -isnot [array]) {
        throw "$ReviewId observation has an invalid refactoring result shape."
    }
    if (-not $Observation.IsSuccess -or @($Observation.Errors).Count -gt 0 -or
        $Observation.parseErrorCount -gt 0 -or @($Observation.ruleFailures).Count -gt 0) {
        return [pscustomobject]@{ status = 'ProbeExecutionFailed'; reason = 'Refactoring failed or recorded parser/rule errors; returned SQL is not evidence of a safe skip.' }
    }
    # These probes run exactly one blocked rule on a fixed fixture. Require byte-for-byte
    # string preservation plus a visible skip; keywords in comments/literals cannot pass.
    $expectedSql = switch ($ReviewId) {
        'R02' { "SELECT * FROM dbo.Users WHERE LTRIM(UserName) = 'admin';" }
        'R19' { 'DECLARE @T TABLE (id int); BEGIN TRANSACTION; INSERT INTO @T VALUES (1); ROLLBACK TRANSACTION; SELECT COUNT(*) FROM @T;' }
        'R20' { 'CREATE TABLE #T (id int); DECLARE @T TABLE (id int); SELECT * FROM @T;' }
    }
    $rule = if ($ReviewId -eq 'R02') { 'REF_RULE_103_TRIM' } else { 'REF_RULE_002_TABLE_VAR' }
    $skip = @($Observation.safetySkips | Where-Object { $_.RuleId -ceq $rule -and
        -not [string]::IsNullOrWhiteSpace($_.ReasonCode) -and -not [string]::IsNullOrWhiteSpace($_.Reason) -and
        -not [string]::IsNullOrWhiteSpace($_.RequiredEvidence) })
    $met = $Observation.OutputSql -ceq $expectedSql -and $Observation.changeCount -eq 0 -and
        $skip.Count -eq 1 -and @($Observation.warnings | Where-Object { $_ -is [string] -and $_.Contains($rule) }).Count -gt 0
    [pscustomobject]@{
        status = $(if ($met) { 'ProbeConditionMet' } else { 'NotMet' })
        reason = $(if (-not $met) { 'The isolated risk guard must preserve the exact fixture, record no changes, and expose its skip reason and prerequisites.' }
            elseif ($Observation.changeCount -eq 0) { 'Successful execution without applied changes preserves the minimum condition; this does not prove SQL equivalence.' }
            else { 'Successful execution preserves the minimum condition; other changes still require semantic acceptance.' })
    }
}

function Get-RedactionProbeCheck {
    param([Parameter(Mandatory)]$Observation)

    $booleanFields = @('CanExport','literalRetained','serverRetained','aliasRetained','tableMasked',
        'noSentinel','sourceUnchanged','payloadWellFormed','unsupportedBlocked','unsupportedHasNoPayload','unsupportedSummarySafe')
    if ($null -eq $Observation.PSObject.Properties['status'] -or $Observation.status -isnot [string]) {
        throw 'R01 observation is missing a valid status.'
    }
    foreach ($name in $booleanFields) {
        if ($null -eq $Observation.PSObject.Properties[$name] -or $Observation.$name -isnot [bool]) {
            throw "R01 observation is missing a boolean $name."
        }
    }
    if ($Observation.status -cne 'Ready' -or -not $Observation.CanExport -or -not $Observation.payloadWellFormed) {
        return [pscustomobject]@{ status='ProbeExecutionFailed'; reason='The supported fixture did not produce a ready, well-formed payload; empty/error output is not evidence of redaction.' }
    }
    $met = -not $Observation.literalRetained -and -not $Observation.serverRetained -and -not $Observation.aliasRetained -and
        $Observation.tableMasked -and $Observation.noSentinel -and $Observation.sourceUnchanged -and
        $Observation.unsupportedBlocked -and $Observation.unsupportedHasNoPayload -and $Observation.unsupportedSummarySafe
    [pscustomobject]@{ status=$(if ($met) { 'ProbeConditionMet' } else { 'NotMet' });
        reason='Minimum synthetic XML redaction and fail-closed checks only; this does not certify arbitrary XML or raw report formats.' }
}

function Test-PlanRowBindings {
    param([Parameter(Mandatory)]$Observation)

    $expectedPaths = @('Views/PlanWorkspaceView.xaml','Views/PlanView.xaml')
    $rows = @($Observation.rowBindings)
    if ($rows.Count -ne 2 -or @(Compare-Object $expectedPaths @($rows.path)).Count -ne 0) { return $false }
    foreach ($view in $rows) {
        if (@($view.outputColumns).Count -ne 1 -or @($view.readColumns).Count -ne 1) { return $false }
        if ($view.outputColumns[0].bindingPath -cne 'ActualRows' -or
            $view.readColumns[0].bindingPath -cne 'ActualRowsRead') { return $false }
    }
    return $true
}

Export-ModuleMember -Function Get-AcceptanceInputSnapshot, Compare-AcceptanceInputSnapshot, Copy-FrozenAcceptanceInput, Get-RefactorProbeCheck, Get-RedactionProbeCheck, Test-PlanRowBindings

function Write-AcceptanceLog {
    param([string]$Path, [ValidateSet('Debug','Release')][string]$Configuration,
        [ValidateSet('DEBUG','WARN','ERROR','CRITICAL')][string]$Level, [string]$Message)
    if ($Configuration -eq 'Release' -and $Level -in @('DEBUG','WARN')) { return }
    $line = "[$Level] $([DateTimeOffset]::UtcNow.ToString('o')) $Message"
    [IO.File]::AppendAllText($Path, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    [Console]::Error.WriteLine($line)
}

function Invoke-AcceptanceProcess {
    param([string]$Executable, [string[]]$ArgumentList, [string]$WorkingDirectory,
        [string]$OutputStem, [ValidateRange(1,3600000)][int]$TimeoutMilliseconds = 900000)
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$Executable; $info.WorkingDirectory=$WorkingDirectory; $info.UseShellExecute=$false
    $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    foreach ($argument in $ArgumentList) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo=$info
    $record = [ordered]@{ executable=$Executable; arguments=$ArgumentList; startedUtc=[DateTimeOffset]::UtcNow.ToString('o');
        finishedUtc=$null; processId=$null; exitCode=-1; timedOut=$false; outputComplete=$false; failure=$null }
    $stdout = $null; $stderr = $null; $started = $false
    try {
        # Stream directly to evidence files so timeout and crashes still retain partial output.
        $stdout = [IO.FileStream]::new("$OutputStem.stdout.txt", [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $stderr = [IO.FileStream]::new("$OutputStem.stderr.txt", [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $started = $process.Start(); $record.processId=$process.Id
        $stdoutTask=$process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $stderrTask=$process.StandardError.BaseStream.CopyToAsync($stderr)
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            $record.timedOut=$true
            $record.failure='Process timed out; requested termination of the process tree.'
            $process.Kill($true)
            if (-not $process.WaitForExit(5000)) { throw [TimeoutException]::new('Process tree did not terminate within 5 seconds.') }
        }
        $record.exitCode=$process.ExitCode
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdoutTask,$stderrTask), 5000)) {
            throw [TimeoutException]::new('Redirected streams did not close within 5 seconds; evidence may be partial.')
        }
        $record.outputComplete=$true
    } catch {
        $record.failure=$_.Exception.ToString()
        if ($started -and -not $process.HasExited) {
            try { $process.Kill($true); [void]$process.WaitForExit(5000) } catch { $record.failure += "`nTermination failed: $($_.Exception.Message)" }
        }
        $cause=$_.Exception.GetBaseException()
        if ($cause -isnot [ComponentModel.Win32Exception] -and $cause -isnot [IO.IOException] -and
            $cause -isnot [UnauthorizedAccessException] -and $cause -isnot [TimeoutException]) { throw }
    } finally {
        if ($null -ne $stdout) { $stdout.Dispose() }
        if ($null -ne $stderr) { $stderr.Dispose() }
        $process.Dispose()
        $record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        # The caller uses a new run/name, never overwrite historical command evidence.
        $json = $record | ConvertTo-Json -Depth 8
        $recordStream=[IO.FileStream]::new("$OutputStem.command.json", [IO.FileMode]::CreateNew)
        try {
            $bytes=[Text.UTF8Encoding]::new($false).GetBytes($json)
            $recordStream.Write($bytes,0,$bytes.Length); $recordStream.Flush($true)
        } finally { $recordStream.Dispose() }
    }
    [pscustomobject]$record
}

Export-ModuleMember -Function Write-AcceptanceLog, Invoke-AcceptanceProcess
