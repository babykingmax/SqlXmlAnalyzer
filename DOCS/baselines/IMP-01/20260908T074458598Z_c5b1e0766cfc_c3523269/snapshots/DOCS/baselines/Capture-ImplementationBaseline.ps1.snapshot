#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryPath = (Join-Path $PSScriptRoot '..\..'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$utf8 = [System.Text.UTF8Encoding]::new($false)
$commands = [System.Collections.Generic.List[object]]::new()
$artifacts = [System.Collections.Generic.List[string]]::new()
$started = [DateTimeOffset]::UtcNow
$excludedPattern = '(?i)(^|/)(\.git|bin|obj|\.vs|publish(?:-[^/]*)?|backups|\.tmp\.[^/]*)(/|$)'

function Invoke-CapturedProcess {
    param([string]$Executable, [string[]]$ArgumentList)
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Executable
    $info.WorkingDirectory = $repository
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = $utf8
    $info.StandardErrorEncoding = $utf8
    foreach ($argument in $ArgumentList) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    $commandStarted = [DateTimeOffset]::UtcNow
    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            executable = $Executable
            arguments = $ArgumentList
            startedUtc = $commandStarted.ToString('o')
            finishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
            exitCode = $process.ExitCode
            stdout = $stdoutTask.GetAwaiter().GetResult()
            stderr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally { $process.Dispose() }
}

function Get-GitText {
    param([string[]]$ArgumentList)
    $result = Invoke-CapturedProcess 'git' (@('-c', 'core.quotepath=false') + $ArgumentList)
    if ($result.exitCode -ne 0) { throw "Git failed: $($result.stderr)" }
    return $result.stdout
}

function Get-PathLines {
    param([string]$Value)
    return @($Value -split '\r?\n' | Where-Object { $_ -ne '' })
}

function Test-InScope {
    param([string]$Path)
    return $Path -notmatch $excludedPattern -and $Path -notmatch '(?i)^DOCS/baselines/IMP-01/'
}

# Start with Git's tracked inventory; never recursively enumerate the workspace.
$trackedText = Get-GitText @('ls-files')
$head = (Get-GitText @('rev-parse', 'HEAD')).Trim()
$runId = '{0}_{1}_{2}' -f $started.ToString('yyyyMMddTHHmmssfffZ'), $head.Substring(0, 12), [guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $repository "DOCS/baselines/IMP-01/$runId"
if (Test-Path -LiteralPath $runDirectory) { throw 'Refusing to overwrite an existing baseline.' }
[void][System.IO.Directory]::CreateDirectory($runDirectory)

function Write-Artifact {
    param([string]$RelativePath, [string]$Content)
    $destination = Join-Path $runDirectory $RelativePath
    [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination))
    [System.IO.File]::WriteAllText($destination, $Content, $utf8)
    if (-not $artifacts.Contains($RelativePath)) { $artifacts.Add($RelativePath) }
}

function Write-JsonArtifact {
    param([string]$RelativePath, [object]$Value)
    Write-Artifact $RelativePath (ConvertTo-Json -InputObject $Value -Depth 20)
}

function Save-Command {
    param([string]$Name, [string]$Executable, [string[]]$ArgumentList)
    $result = Invoke-CapturedProcess $Executable $ArgumentList
    Write-Artifact "$Name.stdout.txt" $result.stdout
    Write-Artifact "$Name.stderr.txt" $result.stderr
    $commands.Add([pscustomobject]@{
        name = $Name; executable = $Executable; arguments = $ArgumentList
        startedUtc = $result.startedUtc; finishedUtc = $result.finishedUtc; exitCode = $result.exitCode
        stdout = "$Name.stdout.txt"; stderr = "$Name.stderr.txt"
    })
    Write-Host "$Name : exit $($result.exitCode)"
    return $result
}

$tracked = @(Get-PathLines $trackedText | Where-Object { Test-InScope $_ })
$untracked = @(Get-PathLines (Get-GitText @('ls-files', '--others', '--exclude-standard')) | Where-Object { Test-InScope $_ })
$inputPaths = @(@($tracked) + @($untracked) | Sort-Object -Unique)
$sourcePaths = @($inputPaths | Where-Object { $_ -match '\.(cs|xaml|csproj)$' })
$configPaths = @($inputPaths | Where-Object { $_ -match '(\.csproj$|\.props$|\.sln$|(^|/)(RuleConfiguration\.json|global\.json|NuGet\.Config|nuget\.config|packages\.lock\.json|\.editorconfig|\.gitattributes|\.gitignore)$|^\.github/workflows/)' })
$fixturePaths = @($inputPaths | Where-Object { $_ -notmatch '^DOCS/' -and $_ -match '\.(sqlplan|xdl|xel|sql)$' })
$changedPaths = @(@(Get-PathLines (Get-GitText @('diff', '--name-only'))) + @(Get-PathLines (Get-GitText @('diff', '--cached', '--name-only'))) | Where-Object { Test-InScope $_ } | Sort-Object -Unique)
$snapshotPaths = @(@($untracked) + @($changedPaths) + @($configPaths) + @($fixturePaths) | Sort-Object -Unique)

Write-Artifact 'git-ls-files.txt' $trackedText
Write-Artifact 'git-index.txt' (Get-GitText @('ls-files', '--stage'))
Write-Artifact 'git-status-before.txt' (Get-GitText @('status', '--short', '--untracked-files=all'))
Write-Artifact 'git-branch.txt' (Get-GitText @('branch', '--show-current'))
Write-Artifact 'source-inventory.txt' (($sourcePaths -join "`n") + "`n")
Write-Artifact 'untracked-inputs.txt' (($untracked -join "`n") + "`n")
Write-Artifact 'working-tree.patch' (Get-GitText (@('diff', '--binary', '--full-index', '--no-ext-diff', '--no-textconv', '--') + $tracked))
Write-Artifact 'index.patch' (Get-GitText (@('diff', '--cached', '--binary', '--full-index', '--no-ext-diff', '--no-textconv', '--') + $tracked))

$manifest = @(
    foreach ($path in $inputPaths) {
        $absolute = Join-Path $repository $path
        $exists = Test-Path -LiteralPath $absolute -PathType Leaf
        [pscustomobject]@{
            path = $path; tracked = $tracked -contains $path; exists = $exists
            bytes = $(if ($exists) { (Get-Item -LiteralPath $absolute).Length } else { $null })
            sha256 = $(if ($exists) { (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash } else { $null })
        }
    }
)
Write-JsonArtifact 'input-manifest.json' $manifest
Write-JsonArtifact 'configuration-manifest.json' @($manifest | Where-Object { $configPaths -contains $_.path })
Write-JsonArtifact 'fixture-manifest.json' @($manifest | Where-Object { $fixturePaths -contains $_.path })
foreach ($path in $snapshotPaths) {
    $source = Join-Path $repository $path
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        # A non-code suffix prevents root SDK globs from compiling archived C#/XAML.
        $relative = "snapshots/$path.snapshot"
        $destination = Join-Path $runDirectory $relative
        [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination))
        [System.IO.File]::Copy($source, $destination, $false)
        $artifacts.Add($relative)
    }
}

$sdk = Save-Command 'sdk-version' 'dotnet' @('--version')
$null = Save-Command 'sdk-info' 'dotnet' @('--info')
$null = Save-Command 'sdk-list' 'dotnet' @('--list-sdks')
$null = Save-Command 'runtime-list' 'dotnet' @('--list-runtimes')
$null = Save-Command 'git-version' 'git' @('--version')
$properties = Save-Command 'evaluated-properties' 'dotnet' @('msbuild', 'SqlXmlAnalyzer.csproj', '-nologo', "-p:Configuration=$Configuration", '-getProperty:Version,AssemblyVersion,InformationalVersion,TargetFramework,Configuration')
Write-JsonArtifact 'environment.json' ([ordered]@{
    repository = $repository; operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    powershell = $PSVersionTable.PSVersion.ToString(); timezoneId = [TimeZoneInfo]::Local.Id
    captureUtc = $started.ToString('o'); captureLocal = $started.ToLocalTime().ToString('o')
    sdk = $sdk.stdout.Trim(); globalJsonPresent = Test-Path -LiteralPath (Join-Path $repository 'global.json')
    configuration = $Configuration
})

$restore = Save-Command 'restore' 'dotnet' @('restore', 'SqlXmlAnalyzer.sln', '--nologo')
$build = Save-Command 'build' 'dotnet' @('build', 'SqlXmlAnalyzer.sln', '--no-restore', '--no-incremental', '--configuration', $Configuration, '--nologo')
$test = $null
$trxRelative = 'test-output/baseline.trx'
$trxPath = Join-Path $runDirectory $trxRelative
if ($build.exitCode -eq 0) {
    $test = Save-Command 'test' 'dotnet' @('test', 'SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj', '--no-build', '--no-restore', '--configuration', $Configuration, '--nologo', '--logger', 'trx;LogFileName=baseline.trx', '--results-directory', (Join-Path $runDirectory 'test-output'))
}
$null = Save-Command 'resolved-packages' 'dotnet' @('list', 'SqlXmlAnalyzer.sln', 'package', '--include-transitive', '--format', 'json')

$testSummary = $null
$failures = @()
if (Test-Path -LiteralPath $trxPath) {
    $artifacts.Add($trxRelative)
    [xml]$trx = [System.IO.File]::ReadAllText($trxPath)
    $counters = $trx.SelectSingleNode('//*[local-name()="Counters"]')
    $testSummary = [ordered]@{}
    foreach ($attribute in $counters.Attributes) { $testSummary[$attribute.Name] = [int]$attribute.Value }
    $failures = @($trx.SelectNodes('//*[local-name()="UnitTestResult" and @outcome!="Passed"]') | ForEach-Object {
        [pscustomobject]@{ testName = $_.GetAttribute('testName'); outcome = $_.GetAttribute('outcome'); detail = $_.InnerText }
    })
}
Write-JsonArtifact 'test-nonpassed.json' $failures

$drift = @(
    foreach ($entry in $manifest) {
        $absolute = Join-Path $repository $entry.path
        $currentHash = if (Test-Path -LiteralPath $absolute -PathType Leaf) { (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash } else { $null }
        if ($entry.sha256 -ne $currentHash) { [pscustomobject]@{ path = $entry.path; before = $entry.sha256; after = $currentHash } }
    }
)
$afterPaths = @(@(Get-PathLines (Get-GitText @('ls-files'))) + @(Get-PathLines (Get-GitText @('ls-files', '--others', '--exclude-standard'))) | Where-Object { Test-InScope $_ } | Sort-Object -Unique)
$pathDrift = @(Compare-Object $inputPaths $afterPaths | Select-Object InputObject, SideIndicator)
$headAfter = (Get-GitText @('rev-parse', 'HEAD')).Trim()
$indexAfter = Get-GitText @('ls-files', '--stage')
Write-JsonArtifact 'input-drift.json' $drift
Write-JsonArtifact 'input-path-drift.json' $pathDrift
Write-Artifact 'git-status-after.txt' (Get-GitText @('status', '--short', '--untracked-files=all'))
Write-JsonArtifact 'commands.json' $commands.ToArray()
$buildCounts = @([regex]::Matches($build.stdout, '(?m)^\s*(\d+)\s*(?:个警告|Warning\(s\)|个错误|Error\(s\))\s*$') | ForEach-Object { [int]$_.Groups[1].Value })
$summary = [ordered]@{
    schemaVersion = 1; step = 'IMP-01'; runId = $runId; head = $head; configuration = $Configuration
    startedUtc = $started.ToString('o'); finishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    trackedInScope = $tracked.Count; untrackedInScope = $untracked.Count; sourceFileCount = $sourcePaths.Count
    trackedCsXamlCsprojCount = @($tracked | Where-Object { $_ -match '\.(cs|xaml|csproj)$' }).Count
    fixtureCount = $fixturePaths.Count; configurationFileCount = $configPaths.Count; inputCount = $manifest.Count
    sdk = $sdk.stdout.Trim(); restoreExitCode = $restore.exitCode; buildExitCode = $build.exitCode
    buildWarnings = $(if ($buildCounts.Count -ge 2) { $buildCounts[-2] } else { $null })
    buildErrors = $(if ($buildCounts.Count -ge 2) { $buildCounts[-1] } else { $null })
    testExitCode = $(if ($null -ne $test) { $test.exitCode } else { $null }); testCounters = $testSummary
    inputDriftCount = $drift.Count; inputPathDriftCount = $pathDrift.Count
    headUnchanged = $headAfter -eq $head; indexUnchanged = $indexAfter -eq [System.IO.File]::ReadAllText((Join-Path $runDirectory 'git-index.txt'))
    sqlServerExecutionPerformed = $false; wpfVisualTestPerformed = $false; coverageGenerated = $false
    note = 'Historical evidence only. No defect remediation, SQL execution, publishing, commit, or tag performed.'
}
Write-JsonArtifact 'summary.json' $summary
$artifactManifest = @($artifacts | Sort-Object -Unique | ForEach-Object {
    $artifactPath = Join-Path $runDirectory $_
    [pscustomobject]@{ path = $_; bytes = (Get-Item -LiteralPath $artifactPath).Length; sha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash }
})
Write-JsonArtifact 'artifact-manifest.json' $artifactManifest
Write-Host "Baseline saved: $runDirectory"
$summary | ConvertTo-Json -Depth 10
if ($restore.exitCode -ne 0 -or $build.exitCode -ne 0 -or $null -eq $test -or $test.exitCode -ne 0 -or $null -eq $testSummary -or $drift.Count -ne 0 -or $pathDrift.Count -ne 0 -or -not $summary.headUnchanged -or -not $summary.indexUnchanged) { exit 1 }
