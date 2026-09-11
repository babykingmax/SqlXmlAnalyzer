param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP26-Review-' + [Guid]::NewGuid().ToString('N'))
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
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../IMP-26/WpfProbe.cs.txt') -Raw
$marker = 'VerifyAsyncBehaviour(main, workspace, args[0], args[1]);'
if (($source.Split($marker, [StringSplitOptions]::None).Length -ne 2) -or -not $source.Contains('internal static class Program')) { throw 'Base WPF probe contract changed.' }
$source = $source.Replace('internal static class Program', 'internal static partial class Program').Replace($marker, $marker + ' VerifyReviewRegressions(main, workspace, args[0], args[1]);')
Set-Content -LiteralPath (Join-Path $probeDirectory 'Program.cs') -Value $source -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ReviewRegressionProbe.cs.txt') -Destination (Join-Path $probeDirectory 'ReviewRegressionProbe.cs')
dotnet build (Join-Path $probeDirectory 'Probe.csproj') -c $Configuration -m:1 -v minimal
if ($LASTEXITCODE -ne 0) { throw 'WPF probe build failed' }
$outputDirectory = Join-Path $PSScriptRoot "wpf-$Configuration"
if (Test-Path -LiteralPath $outputDirectory) {
    $resolved = [IO.Path]::GetFullPath($outputDirectory)
    $archive = [IO.Path]::GetFullPath((Join-Path $repository ('.tmp.imp26-review-' + [Guid]::NewGuid().ToString('N'))))
    if (-not $resolved.StartsWith($repository + [IO.Path]::DirectorySeparatorChar) -or -not $archive.StartsWith($repository + [IO.Path]::DirectorySeparatorChar)) { throw 'Evidence paths escaped repository' }
    Move-Item -LiteralPath $resolved -Destination $archive
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$probe = Start-Process -FilePath (Join-Path $probeDirectory "bin/$Configuration/net8.0-windows/Probe.exe") -ArgumentList @($repository, $outputDirectory) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $outputDirectory 'stdout.log') -RedirectStandardError (Join-Path $outputDirectory 'stderr.log')
if ($probe.ExitCode -ne 0) { throw "WPF probe failed: $outputDirectory/stderr.log" }
Write-Output "WPF evidence: $outputDirectory"
