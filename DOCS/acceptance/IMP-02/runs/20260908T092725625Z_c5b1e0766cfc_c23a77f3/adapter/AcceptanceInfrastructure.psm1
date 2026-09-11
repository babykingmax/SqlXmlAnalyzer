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

    foreach ($name in @('IsSuccess','OutputSql','Errors','parseErrorCount','ruleFailures','changeCount')) {
        if ($null -eq $Observation.PSObject.Properties[$name]) { throw "$ReviewId observation is missing $name." }
    }
    if ($Observation.IsSuccess -isnot [bool] -or $Observation.OutputSql -isnot [string] -or
        ($Observation.parseErrorCount -isnot [int] -and $Observation.parseErrorCount -isnot [long]) -or $Observation.parseErrorCount -lt 0 -or
        ($Observation.changeCount -isnot [int] -and $Observation.changeCount -isnot [long]) -or $Observation.changeCount -lt 0 -or
        $null -eq $Observation.Errors -or $null -eq $Observation.ruleFailures) {
        throw "$ReviewId observation has an invalid refactoring result shape."
    }
    if (-not $Observation.IsSuccess -or @($Observation.Errors).Count -gt 0 -or
        $Observation.parseErrorCount -gt 0 -or @($Observation.ruleFailures).Count -gt 0) {
        return [pscustomobject]@{ status = 'ProbeExecutionFailed'; reason = 'Refactoring failed or recorded parser/rule errors; returned SQL is not evidence of a safe skip.' }
    }
    $sql = $Observation.OutputSql
    $met = switch ($ReviewId) {
        'R02' { $sql -match '\bLTRIM\s*\(' }
        'R19' { $sql -match 'DECLARE\s+@T\s+TABLE' -and $sql -notmatch 'CREATE\s+TABLE\s+#T\b' }
        'R20' { [regex]::Matches($sql,'(?i)CREATE\s+TABLE\s+#T\b').Count -eq 1 -and $sql -notmatch '(?i)DROP\s+TABLE\s+#T\b' }
    }
    [pscustomobject]@{
        status = $(if ($met) { 'ProbeConditionMet' } else { 'NotMet' })
        reason = $(if (-not $met) { 'Successful execution did not preserve the required SQL condition.' }
            elseif ($Observation.changeCount -eq 0) { 'Successful execution without applied changes preserves the minimum condition; this does not prove SQL equivalence.' }
            else { 'Successful execution preserves the minimum condition; other changes still require semantic acceptance.' })
    }
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

Export-ModuleMember -Function Get-AcceptanceInputSnapshot, Compare-AcceptanceInputSnapshot, Copy-FrozenAcceptanceInput, Get-RefactorProbeCheck, Test-PlanRowBindings
