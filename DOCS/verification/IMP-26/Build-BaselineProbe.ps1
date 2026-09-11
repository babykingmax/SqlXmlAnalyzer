param([switch]$Xel)
$ErrorActionPreference = 'Stop'
$snapshot = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'before/host-path.txt') -Raw).Trim()
$binaries = Join-Path $snapshot 'bin/Release/net8.0-windows'
if (-not (Test-Path -LiteralPath (Join-Path $binaries 'SqlXmlAnalyzer.dll'))) { throw 'The pre-change binary snapshot is unavailable; do not rebuild a baseline from current sources.' }
foreach ($expected in (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'before/production-binaries.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash -LiteralPath (Join-Path $binaries $expected.file)).Hash -ne $expected.sha256) { throw 'Baseline production DLL changed' }
}
$source = if ($Xel) { 'before/PerformanceProbe-Xel.cs.txt' } else { 'before/PerformanceProbe.cs.txt' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $source) -Destination (Join-Path $snapshot 'Program.cs')
$references = (Get-ChildItem -LiteralPath $binaries -Filter '*.dll' -File | Where-Object Name -ne 'Probe.dll' | ForEach-Object {
    '<Reference Include="' + $_.BaseName + '"><HintPath>' + [Security.SecurityElement]::Escape($_.FullName) + '</HintPath><Private>false</Private></Reference>'
}) -join ''
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AssemblyName>Probe</AssemblyName></PropertyGroup><ItemGroup>' + $references + '</ItemGroup></Project>'
Set-Content -LiteralPath (Join-Path $snapshot 'Patch.csproj') -Value $project -Encoding UTF8
dotnet build (Join-Path $snapshot 'Patch.csproj') -c Release -o (Join-Path $snapshot 'patched') -v minimal > (Join-Path $PSScriptRoot 'before/probe-build.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Baseline instrumentation build failed' }
# Only the measurement executable changes; production DLLs remain from the pre-change snapshot.
Copy-Item -LiteralPath (Join-Path $snapshot 'patched/Probe.dll') -Destination (Join-Path $binaries 'Probe.dll')
