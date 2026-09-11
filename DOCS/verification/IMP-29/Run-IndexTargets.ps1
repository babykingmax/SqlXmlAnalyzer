#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$BuildEvidence, [ValidateSet('Debug','Release')][string]$Configuration = 'Release', [ValidateSet('15.0','17.0')][string]$Version = '17.0')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$context = Start-AcceptanceStage $repository $BuildEvidence
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Index output must be a new directory.' }
$null = New-Item -ItemType Directory -Path $output
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../IMP-15/verify-localdb.ps1') -Raw
$pattern = '(?m)^\$repository = .*\r?$'
if ([regex]::Matches($source,$pattern).Count -ne 1) { throw 'Index probe adapter contract changed.' }
$assignment = '$repository = ' + "'" + $repository.Replace("'", "''") + "'"
$source = [regex]::Replace($source,$pattern,[Text.RegularExpressions.MatchEvaluator]{ param($match) $assignment })
$source = $source.Replace("'SqlXmlAnalyzerIMP15_'", "'SqlXmlAnalyzerIMP29_'").Replace('^SqlXmlAnalyzerIMP15_', '^SqlXmlAnalyzerIMP29_')
$source = $source.Replace('if ($LASTEXITCODE -ne 0) { Write-Warning "Dedicated instance cleanup needs attention: $instance" }', 'if ($LASTEXITCODE -ne 0) { throw "Dedicated instance cleanup failed: $instance" }')
$copy = Join-Path $output 'Replay.ps1'; [IO.File]::WriteAllText($copy,$source)
& (Get-Process -Id $PID).Path -NoProfile -File $copy -Configuration $Configuration -Version $Version > (Join-Path $output 'replay.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Index DDL replay failed; inspect replay.log.' }
Complete-AcceptanceStage $context $output @('localdb-results.json','replay.log','Replay.ps1')
Write-Output "PASS: 5 quoted/cross-object index targets, rollback and server guard ($Configuration SQL $Version)"
