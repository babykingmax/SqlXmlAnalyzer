#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$BuildEvidence, [ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$context = Start-AcceptanceStage $repository $BuildEvidence
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Compatibility output must be a new directory.' }
$null = New-Item -ItemType Directory -Path $output
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../IMP-28/Run-CompatibilityProbe.ps1') -Raw
$pattern = '(?m)^\$repository = .*\r?$'
if ([regex]::Matches($source,$pattern).Count -ne 1) { throw 'Compatibility probe adapter contract changed.' }
$assignment = '$repository = ' + "'" + $repository.Replace("'", "''") + "'"
$source = [regex]::Replace($source,$pattern,[Text.RegularExpressions.MatchEvaluator]{ param($match) $assignment })
$copy = Join-Path $output 'Replay.ps1'
[IO.File]::WriteAllText($copy,$source)
& (Get-Process -Id $PID).Path -NoProfile -File $copy -Configuration $Configuration > (Join-Path $output 'replay.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Compatibility replay failed; inspect replay.log.' }
Complete-AcceptanceStage $context $output @("latest-$Configuration.json",'replay.log','Replay.ps1')
Write-Output "PASS: CLI/configuration/session compatibility $Configuration"
