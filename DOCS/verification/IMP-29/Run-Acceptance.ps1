#requires -Version 7.0
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository ('.tmp.imp29-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Acceptance output must be a new directory; previous evidence is never reused.' }
$null = New-Item -ItemType Directory -Path $output
$hardware = [ordered]@{CapturedAtUtc=[DateTime]::UtcNow.ToString('O'); OperatingSystem=(Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,TotalVisibleMemorySize); Processor=@(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors); DotnetSdk=(& dotnet --version); PowerShell=$PSVersionTable.PSVersion.ToString()}
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine .NET SDK.' }
$hardware | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'hardware.json') -Encoding utf8
$stages = [Collections.Generic.List[object]]::new()
function Stage([string]$name,[scriptblock]$run) {
    $started = [DateTime]::UtcNow
    try { & $run; $stages.Add(@{Name=$name; Passed=$true; StartedAtUtc=$started.ToString('O'); CompletedAtUtc=[DateTime]::UtcNow.ToString('O')}) }
    catch { $stages.Add(@{Name=$name; Passed=$false; StartedAtUtc=$started.ToString('O'); Error=$_.Exception.Message}); throw }
    finally { $stages | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'stages.json') -Encoding utf8 }
}
Stage 'BuildTests' { & (Join-Path $PSScriptRoot 'Run-BuildTests.ps1') -OutputDirectory (Join-Path $output 'build-tests') }
$buildEvidence = Join-Path $output 'build-tests/build-evidence.json'
foreach ($configuration in 'Debug','Release') {
    Stage "Database-$configuration" { & (Join-Path $PSScriptRoot 'Run-Database.ps1') -BuildEvidence $buildEvidence -Configuration $configuration -OutputDirectory (Join-Path $output "database-$configuration") }
    foreach ($version in '15.0','17.0') {
        Stage "Index-$configuration-$version" { & (Join-Path $PSScriptRoot 'Run-IndexTargets.ps1') -BuildEvidence $buildEvidence -Configuration $configuration -Version $version -OutputDirectory (Join-Path $output "index-$configuration-$version") }
    }
    Stage "Compatibility-$configuration" { & (Join-Path $PSScriptRoot 'Run-Compatibility.ps1') -BuildEvidence $buildEvidence -Configuration $configuration -OutputDirectory (Join-Path $output "compatibility-$configuration") }
    Stage "CliFailures-$configuration" { & (Join-Path $PSScriptRoot 'Run-CliFailures.ps1') -BuildEvidence $buildEvidence -Configuration $configuration -OutputDirectory (Join-Path $output "cli-failures-$configuration") }
    foreach ($probe in '20','21','22','23','24','25','26','27') {
        Stage "Wpf-$configuration-$probe" { & (Join-Path $PSScriptRoot 'Run-Probe.ps1') -BuildEvidence $buildEvidence -Probe $probe -Configuration $configuration -OutputDirectory (Join-Path $output "wpf-$configuration-$probe") }
    }
    Stage "Diagnostics-$configuration" { & (Join-Path $PSScriptRoot 'Run-Probe.ps1') -BuildEvidence $buildEvidence -Probe Diagnostics -Configuration $configuration -OutputDirectory (Join-Path $output "diagnostics-$configuration") }
}
Stage 'CaptureXel' { & (Join-Path $PSScriptRoot 'Capture-Xel.ps1') -BuildEvidence $buildEvidence -OutputDirectory (Join-Path $output 'xel-input') }
$xel = Join-Path $output 'xel-input/multiple.xel'
foreach ($configuration in 'Debug','Release') {
    Stage "Xel-$configuration" { & (Join-Path $PSScriptRoot 'Run-Probe.ps1') -BuildEvidence $buildEvidence -Probe Xel -Configuration $configuration -InputFile $xel -OutputDirectory (Join-Path $output "xel-$configuration") }
}
# Keep performance sequential and after other acceptance work to avoid self-induced contention.
foreach ($size in 100,1000,5000) {
    Stage "Performance-$size" { & (Join-Path $PSScriptRoot 'Run-Probe.ps1') -BuildEvidence $buildEvidence -Probe Performance -ExpectedOperators $size -InputFile (Join-Path $repository "DOCS/verification/IMP-26/fixtures/plan-$size.sqlplan") -OutputDirectory (Join-Path $output "performance-$size") }
}
Stage 'Performance-Xel' { & (Join-Path $PSScriptRoot 'Run-Probe.ps1') -BuildEvidence $buildEvidence -Probe Performance -ExpectedEvents 2 -InputFile $xel -OutputDirectory (Join-Path $output 'performance-xel') }
Write-Output "Automated stages passed: $output. Desktop/DPI/accessibility acceptance and release approval are separate; consult the acceptance matrix."
