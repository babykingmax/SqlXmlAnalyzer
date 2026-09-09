param(
    [string]$RepoRoot = (Join-Path $PSScriptRoot '..\..')
)

$ErrorActionPreference = 'Stop'
$repoPath = (Resolve-Path -LiteralPath $RepoRoot).Path
$projectPath = Join-Path $repoPath 'SqlXmlAnalyzer.csproj'
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Cannot find SqlXmlAnalyzer.csproj under $repoPath"
}

# Synthetic in-process probes only. No database access or SQL file replacement.
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzerReview-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ReviewProbe.cs.txt') -Destination (Join-Path $probeDirectory 'Program.cs')
$escapedProject = [Security.SecurityElement]::Escape($projectPath)
$projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$escapedProject" />
    <PackageReference Include="System.Text.Json" Version="10.0.9" />
  </ItemGroup>
</Project>
"@
$probeProject = Join-Path $probeDirectory 'ReviewProbe.csproj'
Set-Content -LiteralPath $probeProject -Value $projectXml -Encoding UTF8
& dotnet run --project $probeProject --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Review probe failed with exit code $LASTEXITCODE. Files retained at $probeDirectory"
}
Write-Output ('Results: ' + (Join-Path $probeDirectory 'bin\Debug\net8.0-windows\probe-results.json'))
