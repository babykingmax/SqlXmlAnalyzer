#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../../..'),
    [string]$ArtifactDirectory = 'bin/imp08-20260908')

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$probeDirectory = Join-Path $repository 'bin/imp08-theme-ui'
$applicationOutput = Join-Path (Join-Path $repository $ArtifactDirectory) 'bin/SqlXmlAnalyzer/release'
if (-not (Test-Path -LiteralPath (Join-Path $applicationOutput 'SqlXmlAnalyzer.dll'))) { throw 'Build the application in Release first.' }
[void][IO.Directory]::CreateDirectory($probeDirectory)
$source = Join-Path $PSScriptRoot 'ThemeUiProbe.cs.txt'
Copy-Item -LiteralPath $source -Destination (Join-Path $probeDirectory 'Program.cs') -Force
$project = (Get-Content (Join-Path $PSScriptRoot 'ThemeUiProbe.csproj.txt') -Raw).Replace('$(ApplicationOutput)', [Security.SecurityElement]::Escape($applicationOutput))
$projectPath = Join-Path $probeDirectory 'ThemeUiProbe.csproj'
[IO.File]::WriteAllText($projectPath, $project)
dotnet build $projectPath -c Release -p:UseArtifactsOutput=false *> (Join-Path $PSScriptRoot 'ui-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Theme probe build failed; see ui-build.log.' }

$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
$start.ArgumentList.Add((Join-Path $probeDirectory 'bin/Release/net8.0-windows/ThemeUiProbe.dll'))
$start.ArgumentList.Add($repository)
$start.ArgumentList.Add((Join-Path $PSScriptRoot 'ui'))
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$completed = $process.WaitForExit(30000)
if (-not $completed) { $process.Kill($true); $process.WaitForExit() }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ui-stdout.txt'), $stdout.GetAwaiter().GetResult())
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ui-stderr.txt'), $stderr.GetAwaiter().GetResult())
$record = [ordered]@{
    Completed = $completed
    ExitCode = $process.ExitCode
    ProbeSourceSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    ApplicationSha256 = (Get-FileHash -LiteralPath (Join-Path $applicationOutput 'SqlXmlAnalyzer.dll') -Algorithm SHA256).Hash
    CoreSha256 = (Get-FileHash -LiteralPath (Join-Path $applicationOutput 'SqlXmlAnalyzer.Core.dll') -Algorithm SHA256).Hash
    WindowShown = $false
}
$record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'ui-process.json')
$record | ConvertTo-Json
if (-not $completed -or $process.ExitCode -ne 0) { throw 'Theme probe failed; see ui-stderr.txt.' }
