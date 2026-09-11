#requires -Version 7.0
param(
    [Parameter(Mandatory)][ValidateSet('20','21','22','23','24','25','26','27','Performance','Xel','Diagnostics')][string]$Probe,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$BuildEvidence,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$InputFile,
    [int]$ExpectedOperators = 0,
    [int]$ExpectedEvents = 0
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$context = Start-AcceptanceStage $repository $BuildEvidence
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Probe output must be a new directory.' }
if ($Probe -eq 'Performance' -and ($ExpectedOperators -le 0 -and $ExpectedEvents -le 0)) { throw 'Performance acceptance requires an expected operator or event count.' }
$null = New-Item -ItemType Directory -Path $output
$hostDirectory = Join-Path $output 'host'
$null = New-Item -ItemType Directory -Path $hostDirectory
$sources = @{
    '20' = 'IMP-20/WpfProbe.cs.txt'; '21' = 'IMP-21-hardening/WpfProbe.cs.txt'; '22' = 'IMP-22/WpfProbe.cs.txt'
    '23' = 'IMP-23-hardening/WpfProbe.cs.txt'; '24' = 'IMP-24-hardening/WpfProbe.cs.txt'; '25' = 'IMP-25-hardening/WpfProbe.cs.txt'
    '26' = 'IMP-26/WpfProbe.cs.txt'; '27' = 'IMP-27/WpfProbe.cs.txt'; 'Performance' = 'IMP-26/PerformanceProbe.cs.txt'; 'Xel' = 'IMP-29/XelProbe.cs.txt'; 'Diagnostics' = 'IMP-29/DiagnosticProbe.cs.txt'
}
$sourcePath = Join-Path $PSScriptRoot ('../' + $sources[$Probe])
$source = Get-Content -LiteralPath $sourcePath -Raw
$mainPattern = 'private static int Main(string[] args)'
if (-not $source.Contains($mainPattern)) { throw 'Probe entry point changed; review adapter.' }
$source = $source.Replace($mainPattern, 'internal static int Run(string[] args)')
$catchPattern = 'catch \(Exception exception\)\s*\{'
if ([regex]::Matches($source, $catchPattern).Count -ne 1) { throw 'Probe exception boundary changed; review adapter.' }
$source = [regex]::Replace($source, $catchPattern, 'catch (Exception exception) { SqlXmlAnalyzer.Tests.AcceptanceProbeBoundary.Describe(exception);')
if ($Probe -eq '24') {
    # IMP25 changed this toolbar entry from Click to a RoutedCommand. Exercise that current contract.
    $marker = 'entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();'
    if ($source.Split($marker, [StringSplitOptions]::None).Length -ne 2) { throw 'Configuration entry adapter contract changed.' }
    $source = $source.Replace($marker, 'var command = (System.Windows.Input.RoutedCommand)entry.Command; Require(command.CanExecute(null, main), "Configuration command enabled"); command.Execute(null, main); Pump();')
    $replacements = @{
        'internal static class Program' = 'internal static partial class Program';
        'var gate = new GatedAnalysis(analysis);' = 'var gate = new GatedRefactoring(refactor);';
        'new ApplicationOrchestrator(gate, refactor, files,' = 'new ApplicationOrchestrator(analysis, gate, files,';
        'ReferenceEquals(gate.LastInput, committedSource.Input)' = '(committedSource.Input?.Envelope is { } envelope && ReferenceEquals(gate.LastEnvelope, envelope))'
    }
    foreach ($key in $replacements.Keys) {
        if ($source.Split($key, [StringSplitOptions]::None).Length -ne 2) { throw 'Configuration snapshot adapter contract changed.' }
        $source = $source.Replace($key, $replacements[$key])
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConfigurationGate.cs.txt') -Destination (Join-Path $hostDirectory 'ConfigurationGate.cs')
}
if ($Probe -eq '26') {
    $marker = 'VerifyAsyncBehaviour(main, workspace, args[0], args[1]);'
    if ($source.Split($marker, [StringSplitOptions]::None).Length -ne 2) { throw 'IMP26 extension point changed.' }
    $source = $source.Replace('internal static class Program', 'internal static partial class Program').Replace($marker, $marker + ' VerifyReviewRegressions(main, workspace, args[0], args[1]);')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../IMP-26-hardening/ReviewRegressionProbe.cs.txt') -Destination (Join-Path $hostDirectory 'ReviewRegressionProbe.cs')
}
[IO.File]::WriteAllText((Join-Path $hostDirectory 'Program.cs'), $source)
Copy-Item -LiteralPath (Join-Path $repository 'SqlXmlAnalyzer.Tests/AcceptanceProbeBoundary.cs') -Destination $hostDirectory
Copy-Item -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Destination $hostDirectory
@'
internal static class EntryPoint
{
    [System.STAThread]
    private static int Main(string[] args) => SqlXmlAnalyzer.Tests.AcceptanceProbeBoundary.Execute(
        () => Program.Run(args), System.Environment.GetEnvironmentVariable("IMP29_PROBE_OUTPUT")!);
}
'@ | Set-Content -LiteralPath (Join-Path $hostDirectory 'EntryPoint.cs') -Encoding utf8
$app = [Security.SecurityElement]::Escape((Join-Path $repository 'SqlXmlAnalyzer.csproj'))
$analysis = [Security.SecurityElement]::Escape((Join-Path $repository 'src/SqlXmlAnalyzer.Analysis/SqlXmlAnalyzer.Analysis.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="$app"/><ProjectReference Include="$analysis"/></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $hostDirectory 'Probe.csproj') -Encoding utf8
& dotnet restore (Join-Path $hostDirectory 'Probe.csproj') -p:BuildProjectReferences=false -warnaserror > (Join-Path $output 'restore.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Probe restore failed.' }
$null = Invoke-StrictBuild (Join-Path $hostDirectory 'Probe.csproj') $Configuration (Join-Path $output 'build.log') @('-p:BuildProjectReferences=false')
# Verify that the standalone host is loading the exact assemblies accepted by the full build.
foreach ($assembly in @('SqlXmlAnalyzer.dll','SqlXmlAnalyzer.Core.dll','SqlXmlAnalyzer.Analysis.dll','SqlXmlAnalyzer.Application.dll','SqlXmlAnalyzer.Refactoring.dll')) {
    $expected = (Get-FileHash -LiteralPath (Join-Path $repository "bin/$Configuration/net8.0-windows/$assembly")).Hash
    $actual = (Get-FileHash -LiteralPath (Join-Path $hostDirectory "bin/$Configuration/net8.0-windows/$assembly")).Hash
    if ($actual -ne $expected) { throw "Stale probe assembly: $assembly" }
}
$probeArguments = if ($Probe -in @('Performance','Xel')) {
    if (-not $InputFile) { throw 'Probe requires an input file.' }
    @([IO.Path]::GetFullPath($InputFile), (Join-Path $output 'verification.json'))
} else { @($repository, $output) }
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $hostDirectory "bin/$Configuration/net8.0-windows/Probe.exe"))
$start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = 'Hidden'
$start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
$start.Environment['IMP29_PROBE_OUTPUT'] = $output
foreach ($argument in $probeArguments) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::new(); $process.StartInfo = $start
$started = [DateTime]::UtcNow
try {
    $null = $process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(240000)) { $process.Kill($true); $process.WaitForExit(); throw 'Probe exceeded 240 second budget.' }
    [IO.File]::WriteAllText((Join-Path $output 'stdout.log'), $stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $output 'stderr.log'), $stderr.GetAwaiter().GetResult())
    $boundary = Get-Content -LiteralPath (Join-Path $output 'boundary.json') -Raw | ConvertFrom-Json
    if ($Probe -eq 'Diagnostics') {
        if ($process.ExitCode -ne 1 -or $boundary.Passed -ne $false -or -not $boundary.Failure) { throw 'Injected failure was incorrectly accepted.' }
        $incidents = @(Get-ChildItem -LiteralPath (Join-Path $output 'dumps') -Directory)
        if ($incidents.Count -ne 1) { throw 'Expected one diagnostic incident.' }
        Assert-AcceptanceDump (Join-Path $repository "bin/$Configuration/net8.0-windows/SqlXmlAnalyzer.Core.dll") (Join-Path $incidents[0].FullName 'process.dmp')
        $log = Get-Content -LiteralPath (Join-Path $output 'probe.log') -Raw
        if ($log -notmatch '\[ERROR\]' -or $log -notmatch '\[CRITICAL\]' -or $log -match '\[(INFO|VERBOSE)\]') { throw 'Required diagnostic log levels missing or extra levels present.' }
        if ($Configuration -eq 'Debug') { if ($log -notmatch '\[DEBUG\]' -or $log -notmatch '\[WARN\]') { throw 'Missing Debug logs.' } }
        elseif ($log -match '\[(DEBUG|WARN)\]') { throw 'Release logged Debug/Warning.' }
        Write-NewEvidenceJson (Join-Path $output 'verification.json') @{Passed=$true; ExpectedFailureObserved=$true; ValidatedNativeDumps=1; LogPolicyVerified=$true; Configuration=$Configuration}
    }
    elseif ($process.ExitCode -ne 0 -or -not $boundary.Passed) { throw "Probe failed: $output/boundary.json" }
    $verification = Get-Content -LiteralPath (Join-Path $output 'verification.json') -Raw | ConvertFrom-Json
    if ($null -ne $verification.Passed -and -not $verification.Passed) { throw 'Probe reported failure.' }
    if ($null -ne $verification.BindingErrors -and $verification.BindingErrors -ne 0 -and $verification.BindingErrors -ne '') { throw 'WPF binding errors.' }
    if ($Probe -eq 'Performance') {
        if ($verification.SourceSha256 -ne (Get-FileHash -LiteralPath $InputFile).Hash -or $verification.Runs.Count -ne 2) { throw 'Incomplete performance evidence.' }
        foreach ($measurement in $verification.Runs) {
            if ($measurement.State -notin @('Ready','Partial') -or $measurement.Samples -le 0 -or $measurement.TotalMs -le 0 -or
                ($ExpectedOperators -gt 0 -and $measurement.Operators -ne $ExpectedOperators) -or
                ($ExpectedEvents -gt 0 -and $measurement.XelEvents -ne $ExpectedEvents)) { throw 'Performance probe did not analyze the expected input completely.' }
        }
    }
    [ordered]@{ Probe=$Probe; Configuration=$Configuration; Passed=$true; StartedAtUtc=$started.ToString('O'); CompletedAtUtc=[DateTime]::UtcNow.ToString('O'); Source=$sources[$Probe]; SourceSha256=(Get-FileHash -LiteralPath $sourcePath).Hash; GeneratedSourceSha256=(Get-FileHash -LiteralPath (Join-Path $hostDirectory 'Program.cs')).Hash } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'run.json') -Encoding utf8
    $resultFiles = @('run.json','verification.json','boundary.json','build.log','build.log.json','restore.log','stdout.log','stderr.log','probe.log',"host/bin/$Configuration/net8.0-windows/Probe.dll") + @(Get-ChildItem -LiteralPath $hostDirectory -File | ForEach-Object { 'host/' + $_.Name })
    if ($Probe -eq 'Diagnostics') { $resultFiles += @(Get-ChildItem -LiteralPath $incidents[0].FullName -File | ForEach-Object { 'dumps/' + $incidents[0].Name + '/' + $_.Name }) }
    Complete-AcceptanceStage $context $output $resultFiles
    Write-Output "PASS: $Probe $Configuration"
}
finally { $process.Dispose() }
