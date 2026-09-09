param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP16-Review-WPF-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Destination (Join-Path $probeDirectory 'Directory.Packages.props')
$escapedProject = [System.Security.SecurityElement]::Escape((Join-Path $repository 'SqlXmlAnalyzer.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$escapedProject" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $probeDirectory 'Probe.csproj'), $project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WpfProbe.cs.txt') -Destination (Join-Path $probeDirectory 'Program.cs')
dotnet run --project (Join-Path $probeDirectory 'Probe.csproj') --configuration $Configuration -- $repository $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'WPF probe failed; temporary project retained for diagnosis.' }
# Keep the small temporary project for reproducibility; no recursive cleanup of computed paths.
