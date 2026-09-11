#requires -Version 7.0
param([Parameter(Mandatory)][string]$Case, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$null = New-Item -ItemType Directory -Path $OutputDirectory
function MustReject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected the evidence guard to reject this regression.' }
}
if ($Case -eq 'warnings-incremental') {
    $project=Join-Path $OutputDirectory 'Warnings.csproj'
    [IO.File]::WriteAllText($project,'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'Warnings.cs'),'public class WarningFixture { public void Run() { int unused; } }')
    & dotnet build $project -c Release -v minimal > (Join-Path $OutputDirectory 'initial.log') 2>&1
    if ($LASTEXITCODE -ne 0 -or (Get-Content -LiteralPath (Join-Path $OutputDirectory 'initial.log') -Raw) -notmatch 'CS0168') { throw 'Expected an initially successful build with a compiler warning.' }
    $log=Join-Path $OutputDirectory 'strict.log'
    MustReject { Invoke-StrictBuild $project Release $log }
    if ((Get-Content -LiteralPath $log -Raw) -notmatch 'CS0168') { throw 'Rebuild did not report the existing compiler warning.' }
    Write-Output "PASS: $Case"
    return
}
if ($Case -eq 'dump-load-context') {
    $project=Join-Path $OutputDirectory 'Validator.csproj'
    [IO.File]::WriteAllText($project,'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>SameIdentityValidator</AssemblyName></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'Validator.cs'), @'
namespace SqlXmlAnalyzer.Core.Diagnostics;
internal static class MinidumpValidator
{
    internal static void Validate(string path)
    {
#if DEBUG
        throw new System.IO.InvalidDataException("Debug-validator");
#else
        throw new System.IO.InvalidDataException("Release-validator");
#endif
    }
}
'@)
    foreach ($configuration in 'Debug','Release') {
        & dotnet build $project -c $configuration -warnaserror -v minimal > (Join-Path $OutputDirectory "$configuration.log") 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic validator build failed.' }
    }
    # Both binaries have identical assembly identity but different code. Repeat
    # the sequence in one process to catch default-context reuse or conflicts.
    foreach ($configuration in 'Debug','Release','Debug','Release') {
        $caught=$null
        try { Assert-AcceptanceDump (Join-Path $OutputDirectory "bin/$configuration/net8.0/SameIdentityValidator.dll") (Join-Path $OutputDirectory 'unused.dmp') }
        catch { $caught=$_.Exception; while ($caught.InnerException) { $caught=$caught.InnerException } }
        if ($caught -isnot [IO.InvalidDataException] -or $caught.Message -ne "$configuration-validator") { throw 'DUMP validation used the wrong assembly or failed to load it.' }
    }
    Write-Output "PASS: $Case"
    return
}
if ($Case -match '^warnings-(0|1|10|20)-(en-US|zh-CN)$') {
    $count = [int]$Matches[1]; $env:DOTNET_CLI_UI_LANGUAGE = $Matches[2]
    $items = if ($count -eq 0) { '' } else { (1..$count | ForEach-Object { '<W Include="warning-' + $_ + '" />' }) -join '' }
    $project = Join-Path $OutputDirectory 'Warnings.proj'
    [IO.File]::WriteAllText($project, '<Project DefaultTargets="Build"><ItemGroup>' + $items + '</ItemGroup><Target Name="Build"><Warning Condition="&apos;@(W)&apos; != &apos;&apos;" Text="%(W.Identity)" Code="IMP29TEST" /></Target><Target Name="Rebuild" DependsOnTargets="Build" /></Project>')
    $log = Join-Path $OutputDirectory 'build.log'
    if ($count -eq 0) { $null = Invoke-StrictBuild $project Release $log }
    else { MustReject { Invoke-StrictBuild $project Release $log } }
    $receipt = Get-Content -LiteralPath ($log + '.json') -Raw | ConvertFrom-Json
    if (($receipt.ExitCode -eq 0) -ne ($count -eq 0)) { throw 'Native MSBuild warning gate mismatch.' }
    if ($count -gt 0 -and [regex]::Matches((Get-Content -LiteralPath $log -Raw),'IMP29TEST').Count -lt $count) { throw 'Build failed for a reason other than the requested warnings.' }
    Write-Output "PASS: $Case"
    return
}
if ($Case -eq 'git-evidence-visible') {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
    $ignored = @(git -C $repository check-ignore -- 'DOCS/verification/IMP-29/evidence/debug/wpf-20.json' 'DOCS/verification/IMP-29/evidence/release/wpf-20.json')
    if ($LASTEXITCODE -ne 1 -or $ignored.Count) { throw 'Versioned evidence is still ignored.' }
    Write-Output "PASS: $Case"
    return
}
$repo = Join-Path $OutputDirectory 'repository'
$null = New-Item -ItemType Directory -Path $repo
& git -C $repo init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Synthetic repository creation failed.' }
[IO.File]::WriteAllText((Join-Path $repo '.gitignore'), "bin/`n")
[IO.File]::WriteAllText((Join-Path $repo 'Source.cs'), 'old-source')
$modes = foreach ($configuration in 'Debug','Release') {
    foreach ($target in @(
        @{Dir="bin/$configuration/net8.0-windows"; Names=@('SqlXmlAnalyzer')},
        @{Dir="SqlXmlAnalyzer.CLI/bin/$configuration/net8.0"; Names=@('SqlXmlAnalyzer.CLI')},
        @{Dir="SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows"; Names=@('SqlXmlAnalyzer.Tests','SqlXmlAnalyzer','SqlXmlAnalyzer.CLI')}
    )) {
        $null = New-Item -ItemType Directory -Path (Join-Path $repo $target.Dir) -Force
        foreach ($name in $target.Names + @('SqlXmlAnalyzer.Core','SqlXmlAnalyzer.Analysis','SqlXmlAnalyzer.Application','SqlXmlAnalyzer.Refactoring')) {
            [IO.File]::WriteAllText((Join-Path $repo ($target.Dir + '/' + $name + '.dll')), 'old-binary')
        }
    }
    [pscustomobject]@{Configuration=$configuration; Build=@{ExitCode=0; WarningsAsErrors=$true; Arguments=@('-warnaserror','-t:Rebuild')}; Binaries=(Get-AcceptanceBinaries $repo $configuration)}
}
$buildPath = Join-Path $OutputDirectory 'build-evidence.json'
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'full.trx'), 'synthetic recorded result')
Write-NewEvidenceJson $buildPath @{SchemaVersion=1; Passed=$true; Sources=(Get-AcceptanceSources $repo); Modes=@($modes); Results=@((Get-EvidenceHash $OutputDirectory 'full.trx'))}
$context = Start-AcceptanceStage $repo $buildPath
$stageDir = Join-Path $OutputDirectory 'stage'
$null = New-Item -ItemType Directory -Path $stageDir
[IO.File]::WriteAllText((Join-Path $stageDir 'result.json'), '{"Passed":true}')
switch ($Case) {
    'unchanged' { Complete-AcceptanceStage $context $stageDir @('result.json'); $null = Assert-AcceptanceStage $stageDir $context.BuildSha256 @('result.json') }
    'source-changed' { [IO.File]::WriteAllText((Join-Path $repo 'Source.cs'),'new-source'); MustReject { Read-AcceptedBuild $repo $buildPath } }
    'source-added' { [IO.File]::WriteAllText((Join-Path $repo 'Added.cs'),'added'); MustReject { Read-AcceptedBuild $repo $buildPath } }
    'source-deleted' { Remove-Item -LiteralPath (Join-Path $repo 'Source.cs'); MustReject { Read-AcceptedBuild $repo $buildPath } }
    'binary-changed' { [IO.File]::WriteAllText((Join-Path $repo 'SqlXmlAnalyzer.CLI/bin/Release/net8.0/SqlXmlAnalyzer.CLI.dll'),'new-binary'); MustReject { Read-AcceptedBuild $repo $buildPath } }
    'build-result-changed' { [IO.File]::WriteAllText((Join-Path $OutputDirectory 'full.trx'),'changed'); MustReject { Read-AcceptedBuild $repo $buildPath } }
    'build-changed-during-stage' { [IO.File]::AppendAllText($buildPath,' '); MustReject { Complete-AcceptanceStage $context $stageDir @('result.json') } }
    'source-changed-during-stage' { [IO.File]::WriteAllText((Join-Path $repo 'Source.cs'),'new-source'); MustReject { Complete-AcceptanceStage $context $stageDir @('result.json') } }
    'stage-result-changed' { Complete-AcceptanceStage $context $stageDir @('result.json'); [IO.File]::WriteAllText((Join-Path $stageDir 'result.json'),'{"Passed":false}'); MustReject { Assert-AcceptanceStage $stageDir $context.BuildSha256 @('result.json') } }
    'stage-different-build' { Complete-AcceptanceStage $context $stageDir @('result.json'); MustReject { Assert-AcceptanceStage $stageDir ('0' * 64) @('result.json') } }
    'stage-unbound-result' { Complete-AcceptanceStage $context $stageDir @('result.json'); MustReject { Assert-AcceptanceStage $stageDir $context.BuildSha256 @('other.json') } }
    'duplicate-records' { $record=Get-EvidenceHash $repo 'Source.cs'; MustReject { Assert-EvidenceRecords @($record,$record) @($record,$record) } }
    'path-escape' { MustReject { Get-EvidenceHash $repo '../full.trx' } }
    'preserve-existing-evidence' { $hash=(Get-FileHash -LiteralPath $buildPath).Hash; MustReject { Write-NewEvidenceJson $buildPath @{Passed=$true} }; if ((Get-FileHash -LiteralPath $buildPath).Hash -ne $hash) { throw 'Existing evidence overwritten.' } }
    default { throw "Unknown regression case: $Case" }
}
Write-Output "PASS: $Case"
