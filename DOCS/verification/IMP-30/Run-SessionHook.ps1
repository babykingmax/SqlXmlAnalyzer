#requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$CandidateManifestSha256,
    [Parameter(Mandatory)][string]$Restored,
    [Parameter(Mandatory)][string]$RestoredManifestSha256,
    [Parameter(Mandatory)][string]$BuildEvidence,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
. (Join-Path $PSScriptRoot '../IMP-29/AcceptanceEvidence.ps1')
. (Join-Path $PSScriptRoot 'ReleaseTools.ps1')
$null = [Reflection.Assembly]::LoadFrom((Join-Path $repository 'bin/Release/net8.0-windows/SqlXmlAnalyzer.Core.dll'))
$output = Resolve-ReleasePath $OutputDirectory
if (Test-Path -LiteralPath $output) { throw [IO.IOException]::new('Session probe output must be new.') }
$null = New-Item -ItemType Directory -Path $output
$code = [SqlXmlAnalyzer.Core.Release.ReleaseOperation]::Run([Action]{
    $Candidate = Resolve-ReleasePath $Candidate
    $Restored = Resolve-ReleasePath $Restored
    $BuildEvidence = Resolve-ReleasePath $BuildEvidence
    $context = Start-AcceptanceStage $repository $BuildEvidence
    $candidateIdentity = Assert-ReleaseCandidate $Candidate $CandidateManifestSha256 $context.BuildSha256
    $restoredManifest = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Verify($Restored,$RestoredManifestSha256)
    if ($restoredManifest.Kind -cne 'recovery') { throw [IO.InvalidDataException]::new('Session recovery must be a recovery bundle.') }
    Write-NewEvidenceJson (Join-Path $output 'inputs.json') @{
        Candidate=$candidateIdentity; RestoredDirectory=$Restored; RestoredManifestSha256=$RestoredManifestSha256
        RestoredGuiSha256=[SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash((Join-Path $Restored 'application/gui/SqlXmlAnalyzer.exe'))
    }
    $hostDirectory = Join-Path $output 'host'; $null = New-Item -ItemType Directory -Path $hostDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SessionHook.cs.txt') -Destination (Join-Path $hostDirectory 'StartupHook.cs')
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $hostDirectory 'SessionHook.csproj') -Encoding utf8
    Invoke-ReleaseProcess dotnet @('build',(Join-Path $hostDirectory 'SessionHook.csproj'),'-c','Release','-t:Rebuild','-warnaserror','-v','minimal') $output (Join-Path $output 'build') | Out-Null
    $oldHook = $env:DOTNET_STARTUP_HOOKS; $oldSession = $env:IMP30_SESSION; $oldResult = $env:IMP30_SESSION_RESULT
    try {
        $env:DOTNET_STARTUP_HOOKS = Join-Path $hostDirectory 'bin/Release/net8.0/SessionHook.dll'
        $env:IMP30_SESSION = Join-Path $Restored 'data/session.pesession'
        foreach ($app in @(@{Name='candidate';Path=(Join-Path $Candidate 'gui/SqlXmlAnalyzer.exe')},@{Name='restored';Path=(Join-Path $Restored 'application/gui/SqlXmlAnalyzer.exe')})) {
            $env:IMP30_SESSION_RESULT = Join-Path $output ($app.Name + '.json')
            Invoke-ReleaseProcess $app.Path @() $output (Join-Path $output $app.Name) | Out-Null
            $result = Read-EvidenceJson $env:IMP30_SESSION_RESULT
            if ($result.Passed -isnot [bool] -or -not $result.Passed -or $result.Executable -ne $app.Path -or $result.SessionSha256 -ne [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash($env:IMP30_SESSION)) { throw [IO.InvalidDataException]::new('Packaged session reader verification failed.') }
        }
    }
    finally { $env:DOTNET_STARTUP_HOOKS = $oldHook; $env:IMP30_SESSION = $oldSession; $env:IMP30_SESSION_RESULT = $oldResult }
    $null = Assert-ReleaseCandidate $Candidate $CandidateManifestSha256 $context.BuildSha256
    $null = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Verify($Restored,$RestoredManifestSha256)
    $results = @('inputs.json','candidate.json','restored.json','host/StartupHook.cs','host/SessionHook.csproj','host/bin/Release/net8.0/SessionHook.dll') + @(Get-ReleaseProcessEvidence @('candidate','restored','build'))
    Complete-AcceptanceStage $context $output $results
    [Console]::WriteLine("Packaged session readers passed: $output")
},$output)
exit $code
