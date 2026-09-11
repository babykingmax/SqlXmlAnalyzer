param([Parameter(Mandatory)][string]$InputDirectory, [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP23-Probe-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Destination $probeDirectory
$core = [Security.SecurityElement]::Escape((Join-Path $repository 'SqlXmlAnalyzer.Core/SqlXmlAnalyzer.Core.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$core" /></ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $probeDirectory 'Probe.csproj') -Value $project -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'XelProbe.cs.txt') -Destination (Join-Path $probeDirectory 'Program.cs')
dotnet build (Join-Path $probeDirectory 'Probe.csproj') -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw 'XEL probe build failed' }
& (Join-Path $probeDirectory "bin/$Configuration/net8.0/Probe.exe") ([IO.Path]::GetFullPath($InputDirectory)) (Join-Path $PSScriptRoot "real-xel-$Configuration.json")
if ($LASTEXITCODE -ne 0) { throw 'XEL probe failed' }
