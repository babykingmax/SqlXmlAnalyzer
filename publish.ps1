#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildEvidence,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot ('publish/candidate-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)))
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DOCS/verification/IMP-29/AcceptanceEvidence.ps1')
. (Join-Path $PSScriptRoot 'DOCS/verification/IMP-30/ReleaseTools.ps1')
# The accepted full build supplies the diagnostic boundary before any publishing starts.
$core = Join-Path $PSScriptRoot 'bin/Release/net8.0-windows/SqlXmlAnalyzer.Core.dll'
if (-not (Test-Path -LiteralPath $core -PathType Leaf)) { throw 'Run the full Debug/Release build and tests before publishing.' }
$null = [Reflection.Assembly]::LoadFrom($core)
$output = Resolve-ReleasePath $OutputDirectory
$diagnostics = Join-Path $PSScriptRoot ('.tmp.imp30-publish-' + [Guid]::NewGuid().ToString('N'))
$code = [SqlXmlAnalyzer.Core.Release.ReleaseOperation]::Run([Action]{
    $BuildEvidence = Resolve-ReleasePath $BuildEvidence
    $context = Start-AcceptanceStage $PSScriptRoot $BuildEvidence
    if (Test-Path -LiteralPath $output) { throw [IO.IOException]::new('Candidate output must not exist.') }
    $stage = Join-Path $diagnostics 'candidate'
    $null = New-Item -ItemType Directory -Path $stage
    foreach ($project in @(@{Name='gui';Path='SqlXmlAnalyzer.csproj'}, @{Name='cli';Path='SqlXmlAnalyzer.CLI/SqlXmlAnalyzer.CLI.csproj'})) {
        Invoke-ReleaseProcess 'dotnet' @('publish', (Join-Path $PSScriptRoot $project.Path), '-c','Release','-r','win-x64','--self-contained','true',
            '-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:PublishReadyToRun=true',
            '-t:Rebuild;Publish','-warnaserror','-m:1','-v','minimal','-o',(Join-Path $stage $project.Name)) $PSScriptRoot (Join-Path $diagnostics $project.Name) 1200 | Out-Null
    }
    foreach ($file in 'gui/SqlXmlAnalyzer.exe','gui/RuleConfiguration.json','cli/SqlXmlAnalyzer.CLI.exe','cli/RuleConfiguration.json') {
        if (-not (Test-Path -LiteralPath (Join-Path $stage $file) -PathType Leaf)) { throw [IO.InvalidDataException]::new("Missing publish output: $file") }
    }
    $null = New-Item -ItemType Directory -Path (Join-Path $stage 'docs')
    foreach ($file in 'ReleaseNotes.md','UpgradeRollback.md','IssueStatus.md') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "DOCS/release-materials/$file") -Destination (Join-Path $stage "docs/$file")
    }
    Copy-Item -LiteralPath $BuildEvidence -Destination (Join-Path $stage 'build-evidence.json')
    $id = '2.0.0-imp30-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    Write-NewEvidenceJson (Join-Path $stage 'release-status.json') ([ordered]@{
        CandidateId=$id; ApplicationVersion='2.0.0'; Runtime='win-x64'; SelfContained=$true; Signed=$false
        ApprovedForDistribution=$false; Gate='CandidateOnly'; BuildSha256=$context.BuildSha256
        Limitations=@('UI-10 external readers','UI-13 OS DPI/multi-monitor','UI-14 physical keyboard/screen reader','UI-15 performance budget','R02/R19/R20/D06 constrained SQL rewrites')
    })
    $hash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Seal($stage,$id,'candidate')
    $null = Assert-ReleaseCandidate $stage $hash $context.BuildSha256
    $null = Read-AcceptedBuild $PSScriptRoot $BuildEvidence
    if ((Get-FileHash -LiteralPath $BuildEvidence).Hash -ne $context.BuildSha256) { throw [IO.InvalidDataException]::new('Build evidence changed.') }
    [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Restore($stage,$hash,$output,[Threading.CancellationToken]::None)
    Complete-AcceptanceStage $context $diagnostics @('gui.stdout.txt','gui.stderr.txt','gui.command.json','cli.stdout.txt','cli.stderr.txt','cli.command.json','candidate/bundle-manifest.json')
    Write-NewEvidenceJson (Join-Path $diagnostics 'candidate-result.json') ([ordered]@{Candidate=$output;Id=$id;ManifestSha256=$hash;ApprovedForDistribution=$false;BuildSha256=$context.BuildSha256})
    [Console]::WriteLine("Candidate: $output")
    [Console]::WriteLine("Manifest SHA256: $hash")
    [Console]::WriteLine("Evidence: $diagnostics")
    [Console]::WriteLine('Candidate only. Read docs/IssueStatus.md before any distribution; retain the complete directory.')
},$diagnostics)
exit $code
