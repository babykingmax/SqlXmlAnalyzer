param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP22-WPF-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Destination (Join-Path $probeDirectory 'Directory.Packages.props')
$escapedProject = [System.Security.SecurityElement]::Escape((Join-Path $repository 'SqlXmlAnalyzer.csproj'))
$escapedAnalysis = [System.Security.SecurityElement]::Escape((Join-Path $repository 'src/SqlXmlAnalyzer.Analysis/SqlXmlAnalyzer.Analysis.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$escapedProject" /><ProjectReference Include="$escapedAnalysis" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $probeDirectory 'Probe.csproj'), $project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WpfProbe.cs.txt') -Destination (Join-Path $probeDirectory 'Program.cs')
$outputDirectory = Join-Path $PSScriptRoot ('wpf-' + $Configuration + '-' + [Guid]::NewGuid().ToString('N'))
dotnet build (Join-Path $probeDirectory 'Probe.csproj') --configuration $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "WPF probe build failed: $probeDirectory" }
& (Join-Path $probeDirectory "bin/$Configuration/net8.0-windows/Probe.exe") $repository $outputDirectory
if ($LASTEXITCODE -ne 0) { throw "WPF probe failed: $probeDirectory" }
Write-Output "Evidence: $outputDirectory"
