#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$BuildEvidence)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$context = Start-AcceptanceStage $repository $BuildEvidence
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'XEL fixture output must be a new directory.' }
$null = New-Item -ItemType Directory -Path $output
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../IMP-26/Capture-XelFixture.ps1') -Raw
function Assign([string]$text,[string]$name,[string]$value) {
    $pattern = '(?m)^\$' + $name + ' = .*\r?$'
    if ([regex]::Matches($text,$pattern).Count -ne 1) { throw 'XEL capture adapter contract changed.' }
    $assignment = '$' + $name + ' = ' + "'" + $value.Replace("'", "''") + "'"
    [regex]::Replace($text,$pattern,[Text.RegularExpressions.MatchEvaluator]{ param($match) $assignment })
}
$source = Assign $source 'repository' $repository
$source = Assign $source 'output' $output
$source = $source.Replace('IMP26_','IMP29_')
# The two sessions run against an exclusively owned synthetic database; bound their waits.
$marker = '$processes | Wait-Process'
if (-not $source.Contains($marker)) { throw 'XEL capture wait contract changed.' }
$source = $source.Replace($marker, @'
foreach ($ownedProcess in $processes) {
            if (-not $ownedProcess.WaitForExit(45000)) {
                foreach ($pending in $processes) { if (-not $pending.HasExited) { $pending.Kill() } }
                throw 'Synthetic deadlock session exceeded 45 seconds.'
            }
        }
'@)
$copy = Join-Path $output 'Capture.ps1'
[IO.File]::WriteAllText($copy,$source)
& (Get-Process -Id $PID).Path -NoProfile -File $copy > (Join-Path $output 'run.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'XEL capture failed; inspect run.log. Cleanup is attempted in the captured script finally block.' }
Complete-AcceptanceStage $context $output @('xel-fixture.json','multiple.xel','run.log','Capture.ps1')
Write-Output "PASS: captured real two-event XEL at $output/multiple.xel"
