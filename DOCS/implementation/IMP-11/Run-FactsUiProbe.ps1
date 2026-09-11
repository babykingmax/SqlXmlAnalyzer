param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../..'))
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$probe = Join-Path $repository 'bin/imp11-ui-probe'
[void][IO.Directory]::CreateDirectory($probe)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FactsUiProbe.cs.txt') -Destination (Join-Path $probe 'Program.cs') -Force
$project = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'FactsUiProbe.csproj.txt') -Raw).Replace('APP_OUTPUT', (Join-Path $repository 'bin/Release/net8.0-windows'))
[IO.File]::WriteAllText((Join-Path $probe 'FactsUiProbe.csproj'), $project)
dotnet build (Join-Path $probe 'FactsUiProbe.csproj') -c Release *> (Join-Path $PSScriptRoot 'ui-build.log')
if ($LASTEXITCODE -ne 0) { throw 'UI probe build failed; see ui-build.log' }
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add((Join-Path $probe 'bin/Release/net8.0-windows/FactsUiProbe.dll'))
$start.ArgumentList.Add($repository)
$start.ArgumentList.Add($PSScriptRoot)
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$completed = $process.WaitForExit(30000)
if (-not $completed) { $process.Kill($true); $process.WaitForExit() }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ui-stdout.log'), $stdout.GetAwaiter().GetResult())
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ui-stderr.log'), $stderr.GetAwaiter().GetResult())
@{Completed=$completed;ExitCode=$process.ExitCode;ApplicationHash=(Get-FileHash -LiteralPath (Join-Path $repository 'bin/Release/net8.0-windows/SqlXmlAnalyzer.dll')).Hash} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'ui-process.json')
if (-not $completed -or $process.ExitCode -ne 0) { throw 'UI probe failed; see ui-stderr.log' }
