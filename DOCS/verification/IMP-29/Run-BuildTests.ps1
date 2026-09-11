#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Build/test output must be a new directory.' }
$null = New-Item -ItemType Directory -Path $output
$sources = Get-AcceptanceSources $repository
$modes = [Collections.Generic.List[object]]::new()
$records = [Collections.Generic.List[object]]::new()
Push-Location $repository
try {
    & dotnet restore SqlXmlAnalyzer.sln -warnaserror > (Join-Path $output 'restore.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    $records.Add((Get-EvidenceHash $output 'restore.log'))
    foreach ($configuration in 'Debug','Release') {
        $results = Join-Path $output $configuration
        $null = New-Item -ItemType Directory -Path $results
        $build = Invoke-StrictBuild 'SqlXmlAnalyzer.sln' $configuration (Join-Path $results 'build.log')
        $binaries = Get-AcceptanceBinaries $repository $configuration
        & dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c $configuration --no-build --no-restore --logger 'trx;LogFileName=full.trx' --results-directory $results -v minimal > (Join-Path $results 'test.log') 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $configuration" }
        Assert-EvidenceRecords $sources (Get-AcceptanceSources $repository)
        Assert-EvidenceRecords $binaries (Get-AcceptanceBinaries $repository $configuration)
        $modes.Add([pscustomobject]@{Configuration=$configuration; Build=$build; Binaries=$binaries})
        foreach ($file in 'build.log','build.log.json','test.log','full.trx') { $records.Add((Get-EvidenceHash $output "$configuration/$file")) }
        Write-Output "PASS: build/tests $configuration"
    }
    Assert-EvidenceRecords $sources (Get-AcceptanceSources $repository)
    foreach ($mode in $modes) { Assert-EvidenceRecords $mode.Binaries (Get-AcceptanceBinaries $repository $mode.Configuration) }
    Write-NewEvidenceJson (Join-Path $output 'build-evidence.json') ([ordered]@{SchemaVersion=1; Passed=$true; Id=[Guid]::NewGuid().ToString('N'); CompletedAtUtc=[DateTime]::UtcNow.ToString('O'); Sources=$sources; Modes=$modes.ToArray(); Results=$records.ToArray()})
}
finally { Pop-Location }
