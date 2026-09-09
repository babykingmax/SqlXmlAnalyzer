param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP19-Cleanup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'Directory.Packages.props') -Destination (Join-Path $probe 'Directory.Packages.props')
$project = [Security.SecurityElement]::Escape((Join-Path $repo 'src/SqlXmlAnalyzer.Application/SqlXmlAnalyzer.Application.csproj'))
$xml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><AssemblyName>SqlXmlAnalyzer.Tests</AssemblyName><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$project" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), $xml)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CleanupProbe.cs.txt') -Destination (Join-Path $probe 'Program.cs')
dotnet build (Join-Path $probe 'Probe.csproj') -c $Configuration --disable-build-servers -m:1
if ($LASTEXITCODE -ne 0) { throw 'Cleanup probe build failed.' }
$json = & dotnet (Join-Path $probe "bin/$Configuration/net8.0/SqlXmlAnalyzer.Tests.dll")
if ($LASTEXITCODE -ne 0) { throw 'Cleanup probe failed.' }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot ('cleanup-' + $Configuration.ToLowerInvariant() + '.json')), ($json -join [Environment]::NewLine))
Write-Output 'PASS: both server versions; create acknowledgement lost, canceled after commit, pre-dispatch failure; owned database absent.'
