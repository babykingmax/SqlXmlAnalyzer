param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP26-WPF-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Destination $probeDirectory
$appProject = [Security.SecurityElement]::Escape((Join-Path $repository 'SqlXmlAnalyzer.csproj'))
$analysisProject = [Security.SecurityElement]::Escape((Join-Path $repository 'src/SqlXmlAnalyzer.Analysis/SqlXmlAnalyzer.Analysis.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$appProject" /><ProjectReference Include="$analysisProject" /></ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $probeDirectory 'Probe.csproj') -Value $project -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WpfProbe.cs.txt') -Destination (Join-Path $probeDirectory 'Program.cs')
dotnet build (Join-Path $probeDirectory 'Probe.csproj') -c $Configuration -m:1 "-p:IntermediateOutputPath=obj/imp26/$Configuration/" -v minimal
if ($LASTEXITCODE -ne 0) { throw 'WPF probe build failed' }
$outputDirectory = Join-Path $PSScriptRoot "wpf-$Configuration"
# Every run starts with fresh artifacts so an earlier report/screenshot cannot satisfy an assertion.
if (Test-Path -LiteralPath $outputDirectory) {
    $resolved = [IO.Path]::GetFullPath($outputDirectory)
    $archive = [IO.Path]::GetFullPath((Join-Path $repository ('.tmp.imp26-wpf-' + [Guid]::NewGuid().ToString('N'))))
    if (-not $resolved.StartsWith($repository + [IO.Path]::DirectorySeparatorChar) -or -not $archive.StartsWith($repository + [IO.Path]::DirectorySeparatorChar)) { throw 'Evidence paths escaped repository' }
    Move-Item -LiteralPath $resolved -Destination $archive
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$verification = Join-Path $outputDirectory 'verification.json'
if (Test-Path -LiteralPath $verification) { Remove-Item -LiteralPath $verification }
$probe = Start-Process -FilePath (Join-Path $probeDirectory "bin/$Configuration/net8.0-windows/Probe.exe") -ArgumentList @($repository, $outputDirectory) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $outputDirectory 'stdout.log') -RedirectStandardError (Join-Path $outputDirectory 'stderr.log')
if ($probe.ExitCode -ne 0) { throw "WPF probe failed: $outputDirectory/stderr.log" }
Write-Output "WPF evidence: $outputDirectory"
