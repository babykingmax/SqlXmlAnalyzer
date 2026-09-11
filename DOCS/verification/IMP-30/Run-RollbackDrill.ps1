#requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$CandidateManifestSha256,
    [Parameter(Mandatory)][string]$PreviousBundle,
    [Parameter(Mandatory)][string]$PreviousManifestSha256,
    [Parameter(Mandatory)][string]$BuildEvidence,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
. (Join-Path $PSScriptRoot '../IMP-29/AcceptanceEvidence.ps1')
. (Join-Path $PSScriptRoot 'ReleaseTools.ps1')
$base = Join-Path $repository 'bin/Release/net8.0-windows'
foreach ($assembly in 'SqlXmlAnalyzer.Core.dll','SqlXmlAnalyzer.Application.dll','SqlXmlAnalyzer.dll') {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $base $assembly))
}
$output = Resolve-ReleasePath $OutputDirectory
if (Test-Path -LiteralPath $output) { throw [IO.IOException]::new('Drill output must be new.') }
$null = New-Item -ItemType Directory -Path $output
$code = [SqlXmlAnalyzer.Core.Release.ReleaseOperation]::Run([Action]{
    $Candidate = Resolve-ReleasePath $Candidate
    $PreviousBundle = Resolve-ReleasePath $PreviousBundle
    $BuildEvidence = Resolve-ReleasePath $BuildEvidence
    $context = Start-AcceptanceStage $repository $BuildEvidence
    $candidateIdentity = Assert-ReleaseCandidate $Candidate $CandidateManifestSha256 $context.BuildSha256
    $previous = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Verify($PreviousBundle,$PreviousManifestSha256)
    if ($previous.Kind -ne 'recovery') { throw [IO.InvalidDataException]::new('Previous application is not a recovery bundle.') }
    Write-NewEvidenceJson (Join-Path $output 'inputs.json') @{Candidate=$candidateIdentity;PreviousDirectory=$PreviousBundle;PreviousManifestSha256=$PreviousManifestSha256;PreviousId=$previous.Id}
    $backup = Join-Path $output 'backup'
    [SqlXmlAnalyzer.Core.Diagnostics.DiagnosticFileAccess]::CreatePrivateDirectory($backup)
    [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Restore($PreviousBundle,$PreviousManifestSha256,(Join-Path $backup 'application'),[Threading.CancellationToken]::None)
    $data = Join-Path $backup 'data'; $null = New-Item -ItemType Directory -Path $data
    Copy-Item -LiteralPath (Join-Path $backup 'application/gui/RuleConfiguration.json') -Destination (Join-Path $data 'RuleConfiguration.json')
    Copy-Item -LiteralPath (Join-Path $repository 'SqlXmlAnalyzer.Tests/TestData/Compatibility/session-v2.0.pesession') -Destination (Join-Path $data 'session.pesession')
    $live = Join-Path $output 'current.sql'
    [IO.File]::WriteAllText($live,"SELECT N'原 SQL';`r`n",[Text.UTF8Encoding]::new($true))
    $writer = [SqlXmlAnalyzer.Application.Services.SqlWritebackService]::new([SqlXmlAnalyzer.Application.Services.PhysicalSqlWritebackFileSystem]::new(),$null,$null)
    $snapshot = $writer.ReadSnapshot($live,[Threading.CancellationToken]::None)
    $write = $writer.WriteBack($snapshot,"SELECT N'候选 SQL';`r`n",[Threading.CancellationToken]::None)
    if (-not $write.IsSuccess -or -not $write.SourceWritten -or -not $write.BackupVerified) { throw [IO.InvalidDataException]::new('Actual SQL writeback/backup failed.') }
    Copy-Item -LiteralPath $write.BackupPath -Destination (Join-Path $data 'original.sql')
    if ([SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash((Join-Path $data 'original.sql')) -ne $snapshot.Sha256) { throw [IO.InvalidDataException]::new('Original SQL backup mismatch.') }
    $backupHash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Seal($backup,'imp30-upgrade-backup','recovery')
    $restored = Join-Path $output 'restored'
    [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Restore($backup,$backupHash,$restored,[Threading.CancellationToken]::None)
    $oldExe = Join-Path $restored 'application/gui/SqlXmlAnalyzer.exe'
    $oldHash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash($oldExe)
    $newHash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash((Join-Path $Candidate 'gui/SqlXmlAnalyzer.exe'))
    if ($oldHash -eq $newHash) { throw [IO.InvalidDataException]::new('Rollback did not restore a different application.') }
    $session = [SqlXmlAnalyzer.Core.Services.TuningSessionService]::new().Load((Join-Path $restored 'data/session.pesession'))
    if ($session.Snapshots.Count -ne 2 -or $session.PlanA.StatementText -ne 'SELECT 1' -or $session.PlanB.StatementText -ne 'SELECT 2') { throw [IO.InvalidDataException]::new('Restored session content mismatch.') }
    $plan = Join-Path $repository 'SqlXmlAnalyzer.Tests/TestData/Compatibility/plan.sqlplan'
    foreach ($item in @(@{Name='candidate';Cli=(Join-Path $Candidate 'cli/SqlXmlAnalyzer.CLI.exe')},@{Name='restored';Cli=(Join-Path $restored 'application/cli/SqlXmlAnalyzer.CLI.exe')})) {
        Invoke-ReleaseProcess $item.Cli @('--help') $output (Join-Path $output ($item.Name + '-help')) | Out-Null
        $report = Join-Path $output ($item.Name + '-report.json')
        Invoke-ReleaseProcess $item.Cli @('-p',$plan,'--config',(Join-Path $restored 'data/RuleConfiguration.json'),'-f','json','-o',$report) $output (Join-Path $output ($item.Name + '-scan')) | Out-Null
        $json = Read-EvidenceJson $report
        if (@($json).Count -ne 1 -or $json[0].Status -ne 'Passed') { throw [IO.InvalidDataException]::new('Packaged CLI scan/report failed.') }
        Invoke-ReleaseProcess $item.Cli @('read',$plan) $output (Join-Path $output ($item.Name + '-read')) | Out-Null
        $read = Read-EvidenceJson (Join-Path $output ($item.Name + '-read.stdout.txt'))
        if ($read.IsSuccess -isnot [bool] -or -not $read.IsSuccess) { throw [IO.InvalidDataException]::new('Packaged CLI read failed.') }
    }
    $originalHash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash((Join-Path $restored 'data/original.sql'))
    $liveHash = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Hash($live)
    if ($originalHash -ne $snapshot.Sha256 -or $liveHash -ne $write.OutputSha256 -or $originalHash -eq $liveHash) { throw [IO.InvalidDataException]::new('SQL rollback invariant failed.') }
    $null = Assert-ReleaseCandidate $Candidate $CandidateManifestSha256 $context.BuildSha256
    $null = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Verify($backup,$backupHash)
    $null = [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Verify($restored,$backupHash)
    Write-NewEvidenceJson (Join-Path $output 'verification.json') ([ordered]@{
        Passed=$true; Scope='Local file rollback, actual published CLI processes; old WPF session opening is recorded separately.'
        CandidateManifestSha256=$CandidateManifestSha256; PreviousManifestSha256=$PreviousManifestSha256; BackupManifestSha256=$backupHash
        PreviousId=$previous.Id; RestoredGui=$oldExe; RestoredGuiSha256=$oldHash; CandidateGuiSha256=$newHash
        RestoredSessionSnapshots=$session.Snapshots.Count; PlanA=$session.PlanA.StatementText; PlanB=$session.PlanB.StatementText
        OriginalSqlSha256=$snapshot.Sha256; RestoredSqlSha256=$originalHash; CurrentSqlSha256=$liveHash; SqlWritebackBackup=$write.BackupPath
        CurrentSqlPreserved=$true; DatabaseChangesReverted=$false; PackagedCliCommands=6; ApprovedForDistribution=$false
    })
    $results = @('inputs.json','verification.json','candidate-report.json','restored-report.json','backup/bundle-manifest.json','restored/bundle-manifest.json') + @(Get-ReleaseProcessEvidence @('candidate-help','candidate-scan','candidate-read','restored-help','restored-scan','restored-read'))
    Complete-AcceptanceStage $context $output $results
    [Console]::WriteLine("Rollback drill passed: $output")
},$output)
exit $code
