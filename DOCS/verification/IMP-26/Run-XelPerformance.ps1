param([ValidateSet('before','after')][string]$Phase = 'after')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$inputFile = (Get-Content -LiteralPath (Join-Path $repository '.tmp.imp26-real/input-path.txt') -Raw).Trim()
$output = Join-Path $PSScriptRoot $Phase
if ($Phase -eq 'before') { & (Join-Path $PSScriptRoot 'Build-BaselineProbe.ps1') -Xel }
$hostDirectory = (Get-Content -LiteralPath (Join-Path $output 'host-path.txt') -Raw).Trim()
$result = Join-Path $output 'xel-multiple.json'
$process = Start-Process -FilePath (Join-Path $hostDirectory 'bin/Release/net8.0-windows/Probe.exe') -ArgumentList @('"'+$inputFile+'"','"'+$result+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'xel.stdout.log') -RedirectStandardError (Join-Path $output 'xel.stderr.log')
if (-not $process.WaitForExit(90000)) {
    Stop-Process -Id $process.Id
    @{Passed=$false;Error='XEL probe exceeded 90 seconds'} | ConvertTo-Json | Set-Content -LiteralPath $result
    throw 'XEL probe exceeded 90 seconds'
}
@{ ProcessExitCode=$process.ExitCode; BaselineNativePartialNoticeSkipped=($Phase -eq 'before'); Policy='Baseline uses its original reader and event selection pipeline while omitting the blocking native Partial notice; after uses AnalyzeFileAsync and persistent Partial state. App startup auto-open is suppressed in both hosts.' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'xel-dialog-policy.json')
if ($process.ExitCode -ne 0) { throw 'XEL measurement failed' }
