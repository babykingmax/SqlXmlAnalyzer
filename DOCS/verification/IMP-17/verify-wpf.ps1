param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$VerifyFailureHandling
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP17-WPF-' + [Guid]::NewGuid().ToString('N'))
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
$outputDirectory = Join-Path $PSScriptRoot ('wpf-' + $Configuration + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory | Out-Null
$intermediate = 'obj/imp17-probe-' + [Guid]::NewGuid().ToString('N') + '/' + $Configuration + '/'
dotnet build (Join-Path $probeDirectory 'Probe.csproj') --configuration $Configuration --disable-build-servers -m:1 "-p:IntermediateOutputPath=$intermediate"
if ($LASTEXITCODE -ne 0) { throw "WPF probe build failed; project retained at $probeDirectory" }
$probeExecutable = Join-Path $probeDirectory "bin/$Configuration/net8.0-windows/Probe.exe"
if ($VerifyFailureHandling) {
    & (Join-Path $PSScriptRoot 'Test-WpfProbeFailureHandling.ps1') -ProbeExecutable $probeExecutable -Repository $repository -EvidenceDirectory $outputDirectory -Configuration $Configuration
} else {
    & $probeExecutable $repository $outputDirectory (Join-Path $probeDirectory 'diagnostics')
    if ($LASTEXITCODE -ne 0) { throw "WPF probe failed with exit code $LASTEXITCODE; project and diagnostics retained at $probeDirectory" }
}
Write-Output "Evidence: $outputDirectory"
# Keep the small temporary project for reproducibility; no recursive cleanup of computed paths.
