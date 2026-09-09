#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../..'))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$utf8 = [System.Text.UTF8Encoding]::new($false)
$commandRecords = [System.Collections.Generic.List[object]]::new()
$artifacts = [System.Collections.Generic.List[string]]::new()
# Git inventory first; no recursive directory enumeration.
$tracked = @(git -C $repository -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot obtain Git inventory.' }
$head = git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot obtain HEAD.' }
$head = $head.Trim()
$runId = '{0}_{1}_{2}' -f [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), $head.Substring(0,12), [guid]::NewGuid().ToString('N').Substring(0,8)
$runDirectory = Join-Path $PSScriptRoot "runs/$runId"
if (Test-Path -LiteralPath $runDirectory) { throw 'Existing run will not be overwritten.' }
[void][IO.Directory]::CreateDirectory($runDirectory)

function Save-Text([string]$Name, [string]$Value) {
    $path = Join-Path $runDirectory $Name
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $Value, $utf8)
    if (-not $artifacts.Contains($Name)) { $artifacts.Add($Name) }
}
function Save-Json([string]$Name, [object]$Value) {
    Save-Text $Name (ConvertTo-Json -InputObject $Value -Depth 20)
}
function Run-Command([string]$Name, [string]$Executable, [string[]]$ArgumentList) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$Executable; $info.WorkingDirectory=$repository; $info.UseShellExecute=$false
    $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.StandardOutputEncoding=$utf8; $info.StandardErrorEncoding=$utf8
    foreach ($argument in $ArgumentList) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo=$info
    $started=[DateTimeOffset]::UtcNow
    try {
        [void]$process.Start()
        $stdoutTask=$process.StandardOutput.ReadToEndAsync(); $stderrTask=$process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout=$stdoutTask.GetAwaiter().GetResult(); $stderr=$stderrTask.GetAwaiter().GetResult()
        Save-Text "$Name.stdout.txt" $stdout; Save-Text "$Name.stderr.txt" $stderr
        $record=[pscustomobject]@{ name=$Name; executable=$Executable; arguments=$ArgumentList; startedUtc=$started.ToString('o'); finishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); exitCode=$process.ExitCode }
        $commandRecords.Add($record)
        Write-Host "$Name : exit $($process.ExitCode)"
        return [pscustomobject]@{ exitCode=$process.ExitCode; stdout=$stdout; stderr=$stderr }
    } finally { $process.Dispose() }
}
function Require-Success([object]$Result, [string]$Stage) {
    if ($Result.exitCode -ne 0) { throw "$Stage failed. See captured stdout/stderr; acceptance checks not completed." }
}
function Assert-StableInputs([string]$Stage) {
    $current = Get-AcceptanceInputSnapshot -RepositoryPath $repository -AdditionalPaths $historicalPaths
    $changes = @(Compare-AcceptanceInputSnapshot -Before $initialInputs -After $current)
    Save-Json "input-checks/$Stage.json" ([ordered]@{ stage=$Stage; snapshot=$current; drift=$changes })
    if ($changes.Count -ne 0) {
        Save-Json 'protected-input-drift.json' $changes
        throw "Protected input inventory, content or HEAD changed at $Stage. Start a fresh run."
    }
    foreach ($entry in $snapshotManifest) {
        if ((Get-FileHash -LiteralPath (Join-Path $runDirectory $entry.path) -Algorithm SHA256).Hash -cne $entry.sha256) {
            throw "Frozen adapter changed at ${Stage}: $($entry.path)"
        }
    }
}

try {
    # Keep original bytes and use only these snapshots for all later reads/compilation.
    $adapterDirectory = Join-Path $runDirectory 'adapter'
    [void][IO.Directory]::CreateDirectory($adapterDirectory)
    $snapshotManifest = @(foreach ($name in @('AcceptanceProbe.cs.txt','PlanRowBindingInspector.cs.txt','AcceptanceInfrastructure.psm1','Test-AcceptanceInfrastructure.ps1','acceptance-matrix.json','Run-AcceptanceMatrix.ps1')) {
        $relative = "adapter/$name"
        $destination = Join-Path $runDirectory $relative
        [IO.File]::Copy((Join-Path $PSScriptRoot $name), $destination, $false)
        $artifacts.Add($relative)
        [pscustomobject]@{ path=$relative; sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
    })
    Save-Json 'adapter-snapshot-manifest.json' $snapshotManifest
    Import-Module (Join-Path $adapterDirectory 'AcceptanceInfrastructure.psm1') -Force
    $matrix=Get-Content (Join-Path $adapterDirectory 'acceptance-matrix.json') -Raw | ConvertFrom-Json
    $expectedIds=@(1..22 | ForEach-Object { 'R{0:D2}' -f $_ })
    if (@(Compare-Object $expectedIds @($matrix.cases.reviewId)).Count -ne 0 -or $matrix.cases.Count -ne 22) { throw 'Matrix must contain R01-R22 exactly once.' }
    $historicalPaths = @('DOCS/review-evidence/ReviewProbe.cs.txt','DOCS/review-evidence/Run-ReviewProbe.ps1','DOCS/review-evidence/baseline.json','DOCS/review-evidence/probe-results.json')
    $initialInputs = Get-AcceptanceInputSnapshot -RepositoryPath $repository -AdditionalPaths $historicalPaths
    if ($initialInputs.head -cne $head) { throw 'HEAD changed during initialization.' }
    Save-Json 'protected-inputs.json' $initialInputs.inputs
    Save-Json 'input-snapshot-before.json' $initialInputs
    Save-Text 'git-status-before.txt' ((git -C $repository status --short) -join "`n")
    $regressions=Run-Command 'infrastructure-tests' 'pwsh' @('-NoProfile','-File',(Join-Path $adapterDirectory 'Test-AcceptanceInfrastructure.ps1'),'-OutputPath',(Join-Path $runDirectory 'infrastructure-tests.json')); Require-Success $regressions 'Acceptance infrastructure regressions'
    $artifacts.Add('infrastructure-tests.json')
    $sdk=Run-Command 'sdk' 'dotnet' @('--info'); Require-Success $sdk 'SDK metadata'
    $restore=Run-Command 'restore' 'dotnet' @('restore','SqlXmlAnalyzer.sln','--nologo'); Require-Success $restore 'Restore'
    $build=Run-Command 'build' 'dotnet' @('build','SqlXmlAnalyzer.sln','--no-restore','--no-incremental','--nologo'); Require-Success $build 'Build'
    Assert-StableInputs 'after-build'
    $test=Run-Command 'test' 'dotnet' @('test','SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj','--no-build','--no-restore','--nologo','--logger','trx;LogFileName=existing-suite.trx','--results-directory',(Join-Path $runDirectory 'test-output')); Require-Success $test 'Existing suite'
    $artifacts.Add('test-output/existing-suite.trx')
    Assert-StableInputs 'after-test'

    $temporaryDirectory=Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP02-'+[guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($temporaryDirectory)
    $compiledInputs = @(foreach ($pair in @(@('AcceptanceProbe.cs.txt','Program.cs'),@('PlanRowBindingInspector.cs.txt','PlanRowBindingInspector.cs'))) {
        $entry = $snapshotManifest | Where-Object path -EQ "adapter/$($pair[0])"
        $destination = Join-Path $temporaryDirectory $pair[1]
        Copy-FrozenAcceptanceInput -SnapshotPath (Join-Path $runDirectory $entry.path) -DestinationPath $destination -ExpectedHash $entry.sha256
        [pscustomobject]@{ snapshot=$entry.path; compiledPath=$destination; sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
    })
    Save-Json 'compiled-inputs.json' $compiledInputs
    $reference=[Security.SecurityElement]::Escape((Join-Path $repository 'SqlXmlAnalyzer.csproj'))
    $project=@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$reference" /><PackageReference Include="System.Text.Json" Version="10.0.9" /></ItemGroup>
</Project>
"@
    $projectPath=Join-Path $temporaryDirectory 'AcceptanceProbe.csproj'
    [IO.File]::WriteAllText($projectPath,$project,$utf8)
    Save-Text 'adapter/AcceptanceProbe.csproj.snapshot' $project
    $probeBuild=Run-Command 'probe-build' 'dotnet' @('build',$projectPath,'--nologo'); Require-Success $probeBuild 'Probe build'
    Assert-StableInputs 'after-probe-build'
    $probe=Run-Command 'probe' 'dotnet' @('run','--project',$projectPath,'--no-build','--no-restore','--',$repository,$runDirectory); Require-Success $probe 'Probe adapter'
    $artifacts.Add('observed.json')
    $observations=@(Get-Content (Join-Path $runDirectory 'observed.json') -Raw | ConvertFrom-Json)
    if ($observations.Count -ne 22 -or @(Compare-Object $expectedIds @($observations.id)).Count -ne 0) { throw 'Unexpected probe result shape.' }
    $cli=Run-Command 'cli-invalid-input' 'dotnet' @('run','--project','SqlXmlAnalyzer.CLI','--no-build','--no-restore','--','--path',(Join-Path $runDirectory 'fixtures/not-a-plan.sqlplan'),'--format','json')
    $cliReport=@($cli.stdout | ConvertFrom-Json)
    if ($cliReport.Count -ne 1 -or [string]::IsNullOrWhiteSpace($cliReport[0].Status)) { throw 'CLI did not return one structured scan result.' }

    $checks=@(foreach ($case in $matrix.cases) {
        $o=($observations | Where-Object id -EQ $case.reviewId).observed
        $met=$false
        $refactorCheck=$null
        switch ($case.reviewId) {
            'R01' { $met=(-not $o.literalRetained -and -not $o.serverRetained -and -not $o.aliasRetained -and $o.tableMasked) }
            'R02' { $refactorCheck=Get-RefactorProbeCheck -ReviewId 'R02' -Observation $o }
            'R03' { $met=(-not $o.IsSuccess -and -not $o.SourceWritten -and $o.BackupAttempted) }
            'R04' { $met=($o.unrelatedXmlIssues -gt 0 -and $cli.exitCode -ne 0 -and $cliReport[0].Status -eq 'Failed') }
            'R05' { $met=($o.openKind -eq 'DeadlockXml' -and $o.parserAccepts) }
            'R06' { $met=($o.parsedPriority -eq '7') }
            'R07' { $met=(@($o.patterns | Where-Object { $_ -match 'SERIALIZABLE|范围锁死锁|Range Lock Deadlock' }).Count -eq 0) }
            'R08' { $met=($o.graphContainsC -and $o.timelineContainsC) }
            'R09' { $met=($null -eq $o.rowMismatch -and $null -eq $o.cardinalityError) }
            'R10' { $met=($o.extractedResidual -eq '[T].[V]=(2)' -and -not [string]::IsNullOrWhiteSpace($o.ruleHit) -and $o.uiResidual -contains '[T].[V]=(2)') }
            'R11' { $met=($o.ExecutionMode -eq 'Batch' -and $o.IsParallel -and $o.Ordered -eq 'True' -and $o.ActualRows -eq '100' -and ($o.ActualRowsRead -replace ',','') -eq '1000') }
            'R12' { $met=([Math]::Abs([double]$o.lowThenHigh-[double]$o.highThenLow) -lt 0.000001) }
            'R13' { $met=($o.escaped -ceq '[Order.Detail]') }
            'R14' { $met=($o.matched.Count -eq 2 -and $o.matched[0] -eq 'sales' -and $o.matched[1] -eq 'audit') }
            'R15' { $met=($o.estimateVsActual.Count -eq 2 -and @($o.estimateVsActual | Where-Object { $null -ne $_.Delta -or $null -ne $_.Value }).Count -eq 0) }
            'R16' { $met=($o.comparedCostA -eq 2 -and $o.comparedCostB -eq 999) }
            'R17' { $met=($o.parentTable -ne 'ChildTable' -and $o.parentPredicates -notcontains 'ChildFilter') }
            'R18' { $met=($o.simpleTwoProcessCycleCount -eq 1) }
            'R19' { $refactorCheck=Get-RefactorProbeCheck -ReviewId 'R19' -Observation $o }
            'R20' { $refactorCheck=Get-RefactorProbeCheck -ReviewId 'R20' -Observation $o }
            'R21' { $met=Test-PlanRowBindings -Observation $o }
            'R22' { $met=($o.initialDescription -notmatch '回放死锁形成过程' -and $o.initialDescription -match '推演') }
            default { throw "No check for $($case.reviewId)" }
        }
        [pscustomobject]@{
            caseId=$case.caseId; reviewId=$case.reviewId; priority=$case.priority
            status=$(if ($null -ne $refactorCheck) { $refactorCheck.status } elseif ($met) { 'ProbeConditionMet' } else { 'NotMet' })
            reason=$(if ($null -ne $refactorCheck) { $refactorCheck.reason } else { $null })
            expected=$case.expected; observed=$o
            adapterRequiresMigration=($case.reviewId -in @('R14','R15','R16'))
            limitations=$case.gap; issueClosed=$false
        }
    })
    Save-Json 'acceptance-results.json' $checks
    $fixtureManifest=@(
        foreach ($kind in @('fixtures','candidates')) {
            foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $runDirectory $kind) -File)) {
                $relative="$kind/$($file.Name)"; $artifacts.Add($relative)
                [pscustomobject]@{ path=$relative; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash; bytes=$file.Length; origin='Synthetic review probe / current engine output'; sqlServerVersion=$null; xsdValidated=$false; privacy='Synthetic test values only; not real production data'; sqlServerExecuted=$false }
            }
        }
        foreach ($path in @($tracked | Where-Object { $_ -match '^SqlXmlAnalyzer.Tests/(TestData|Resources)/.*\.(sqlplan|xdl|xel)$' })) {
            [pscustomobject]@{ path=$path; sha256=(Get-FileHash -LiteralPath (Join-Path $repository $path) -Algorithm SHA256).Hash; bytes=(Get-Item -LiteralPath (Join-Path $repository $path)).Length; origin='Existing repository fixture; acquisition provenance unverified'; sqlServerVersion=$null; xsdValidated=$false; privacy='Requires provenance/privacy review before external sharing'; sqlServerExecuted=$false }
        }
    )
    Save-Json 'fixture-manifest.json' $fixtureManifest
    Assert-StableInputs 'after-probe-and-cli'
    foreach ($entry in $compiledInputs) {
        if ((Get-FileHash -LiteralPath $entry.compiledPath -Algorithm SHA256).Hash -cne $entry.sha256) { throw "Compiled probe input changed: $($entry.compiledPath)" }
    }
    $drift=@()
    Save-Json 'protected-input-drift.json' $drift
    Save-Text 'git-status-after.txt' ((git -C $repository status --short) -join "`n")
    [xml]$trx=Get-Content (Join-Path $runDirectory 'test-output/existing-suite.trx') -Raw
    $counters=$trx.SelectSingleNode('//*[local-name()="Counters"]')
    $summary=[ordered]@{
        schemaVersion=2; adapterVersion='2.0'; step='IMP-02'; runId=$runId; head=$head; runDirectory=$runDirectory
        matrixCases=$checks.Count; probeConditionsMet=@($checks | Where-Object status -EQ 'ProbeConditionMet').Count; notMet=@($checks | Where-Object status -EQ 'NotMet').Count
        probeExecutionFailed=@($checks | Where-Object status -EQ 'ProbeExecutionFailed').Count
        probeConditionsAllMet=(@($checks | Where-Object status -NE 'ProbeConditionMet').Count -eq 0)
        infrastructureTests=(Get-Content (Join-Path $runDirectory 'infrastructure-tests.json') -Raw | ConvertFrom-Json | Select-Object total,passed,failed)
        issuesClosed=0; existingSuite=@{ total=[int]$counters.GetAttribute('total'); passed=[int]$counters.GetAttribute('passed'); failed=[int]$counters.GetAttribute('failed'); notExecuted=[int]$counters.GetAttribute('notExecuted') }
        cliInvalidInputExitCode=$cli.exitCode; protectedInputDrift=$drift; fixtureAndCandidateEntries=$fixtureManifest.Count
        sqlServerExecutionPerformed=$false; wpfVisualTestPerformed=$false; coverageGenerated=$false
        note='Exit 1 means NotMet or ProbeExecutionFailed; neither is a passing condition. Full issue closure requires additional matrix scenarios.'
    }
    Save-Json 'summary.json' $summary
    Save-Json 'commands.json' $commandRecords.ToArray()
    $artifactManifest=@($artifacts | Sort-Object -Unique | ForEach-Object { [pscustomobject]@{ path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $runDirectory $_) -Algorithm SHA256).Hash } })
    Save-Json 'artifact-manifest.json' $artifactManifest
    Write-Host "Acceptance evidence: $runDirectory"
    $summary | ConvertTo-Json -Depth 10
    if (-not $summary.probeConditionsAllMet) { exit 1 }
    exit 0
}
catch {
    Save-Text 'infrastructure-error.txt' $_.ToString()
    Save-Json 'commands.json' $commandRecords.ToArray()
    Write-Error -ErrorAction Continue "Acceptance infrastructure failed: $($_.Exception.Message). Evidence: $runDirectory"
    exit 2
}
