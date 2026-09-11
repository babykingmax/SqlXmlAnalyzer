param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$fixtures = Join-Path $repository 'SqlXmlAnalyzer.Tests/TestData/Compatibility'
$runName = $Configuration + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$run = Join-Path $PSScriptRoot "runs/$runName"
$null = New-Item -ItemType Directory -Path $run
$cli = Join-Path $repository "SqlXmlAnalyzer.CLI/bin/$Configuration/net8.0/SqlXmlAnalyzer.CLI.dll"
$checks = [Collections.Generic.List[string]]::new()
$commands = [Collections.Generic.List[object]]::new()
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw "Compatibility probe failed: $name" }
    $checks.Add($name)
}
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function Run-Cli([string]$name, [string[]]$arguments, [int]$expected) {
    $stdout = Join-Path $run "$name.stdout.json"
    $stderr = Join-Path $run "$name.stderr.log"
    & dotnet $cli @arguments 1> $stdout 2> $stderr
    $code = $LASTEXITCODE
    $commands.Add([ordered]@{ Name = $name; Arguments = $arguments; ExitCode = $code; ExpectedExitCode = $expected; Stdout = "$name.stdout.json"; Stderr = "$name.stderr.log" })
    Check ($code -eq $expected) "$name exit $expected"
    return $stdout
}

Copy-Item -LiteralPath (Join-Path $fixtures 'plan.sqlplan') -Destination (Join-Path $run 'plan.sqlplan')
Copy-Item -LiteralPath (Join-Path $fixtures 'query.sql') -Destination (Join-Path $run 'query.sql')
$plan = Join-Path $run 'plan.sqlplan'
$sql = Join-Path $run 'query.sql'
$planHash = Hash $plan; $sqlHash = Hash $sql
$scanPath = Run-Cli 'scan' @('-p', $plan, '-f', 'json') 0
$scan = Get-Content -LiteralPath $scanPath -Raw | ConvertFrom-Json
Check ($scan.Count -eq 1 -and $scan[0].Status -eq 'Passed' -and $scan[0].MaxSubtreeCostState -eq 'Available') 'scan legacy array and complete cost'
Check ($scan[0].DiagnosticReport.SchemaVersion -eq 'IMP22-1.0' -and $scan[0].Diagnostics.ProtocolVersion -eq '1.0') 'report and rule protocol versions'
$null = Run-Cli 'threshold' @('--path', $plan, '--max-cost', '0.5', '--format', 'json') 1
$readPath = Run-Cli 'read' @('read', $plan) 0
$read = Get-Content -LiteralPath $readPath -Raw | ConvertFrom-Json
Check ($read.IsSuccess -and $read.Plan.Batches.Count -eq 1) 'read model envelope'
$refactorPath = Run-Cli 'refactor' @('refactor', $sql, '--format', 'json') 0
$refactor = Get-Content -LiteralPath $refactorPath -Raw | ConvertFrom-Json
Check ($refactor.ProposalSchemaVersion -eq '1' -and $refactor.Outcome -eq 'CandidateGenerated' -and -not $refactor.SourceWritten -and -not $refactor.CanApply) 'refactor candidate-only contract'
$futureConfiguration = Join-Path $run 'future.json'
Set-Content -LiteralPath $futureConfiguration -Value '{"ConfigurationSchemaVersion":"99","Rules":[]}' -Encoding utf8
$null = Run-Cli 'future-config' @('--path', $plan, '--config', $futureConfiguration, '--format', 'json') 2
Check ((Get-Content -LiteralPath (Join-Path $run 'future-config.stderr.log') -Raw).Contains('CONFIG_SCHEMA_UNSUPPORTED')) 'future configuration recognizable rejection'
$null = Run-Cli 'unknown-option' @('--unknown-option') 2
$existing = Join-Path $run 'existing-report.json'; Set-Content -LiteralPath $existing -Value 'sole-old-report' -Encoding utf8
$oldReportHash = Hash $existing
$null = Run-Cli 'existing-output' @('-p', $plan, '-f', 'json', '-o', $existing) 2
Check ((Hash $existing) -eq $oldReportHash) 'existing report retained'
Check ((Hash $plan) -eq $planHash -and (Hash $sql) -eq $sqlHash) 'CLI original plan and SQL bytes retained'

$base = Join-Path $repository "bin/$Configuration/net8.0-windows"
$null = [Reflection.Assembly]::LoadFrom((Join-Path $base 'SqlXmlAnalyzer.Core.dll'))
$null = [Reflection.Assembly]::LoadFrom((Join-Path $base 'SqlXmlAnalyzer.dll'))
[SqlXmlAnalyzer.Logger]::Shutdown()
[SqlXmlAnalyzer.Logger]::Initialize($false, $null, (Join-Path $run 'session.log'), $true)
try {
    $service = [SqlXmlAnalyzer.Core.Services.TuningSessionService]::new()
    $oldSession = Join-Path $run 'legacy.pesession'
    Copy-Item -LiteralPath (Join-Path $fixtures 'session-v2.0.pesession') -Destination $oldSession
    $oldSessionHash = Hash $oldSession
    $legacy = $service.Load($oldSession)
    Check ($legacy.SourceVersion -eq '2.0' -and $legacy.RequiresMigration) 'legacy session version recognized'
    $convertedSession = Join-Path $run 'converted.pesession'
    $service.Save($convertedSession, $legacy.Snapshots, $legacy.PlanA, $legacy.PlanB, $legacy.ComparisonSelection)
    $current = $service.Load($convertedSession)
    Check ($current.SourceVersion -eq '2.1' -and -not $current.RequiresMigration -and $current.Snapshots.Count -eq 2) 'session conversion readable as current'
    Check ($current.PlanA.StatementText -eq 'SELECT 1' -and $current.PlanB.StatementText -eq 'SELECT 2') 'session comparison pair retained'
    $rejected = $false
    try { $service.Save($oldSession, $legacy.Snapshots, $legacy.PlanA, $legacy.PlanB, $legacy.ComparisonSelection) }
    catch { $rejected = $_.Exception.GetBaseException().Message.Contains('SESSION_DESTINATION_EXISTS') }
    Check ($rejected -and (Hash $oldSession) -eq $oldSessionHash) 'session overwrite rejected and original bytes retained'
    $unsupported = Join-Path $run 'future.pesession'
    $future = [SqlXmlAnalyzer.SafeXmlHelper]::LoadSafe($oldSession)
    $future.Root.SetAttributeValue('Version', '99')
    $future.Save($unsupported)
    $futureHash = Hash $unsupported
    $rejected = $false
    try { $null = $service.Load($unsupported) }
    catch { $rejected = $_.Exception.GetBaseException().Message.Contains('SESSION_VERSION_UNSUPPORTED') }
    Check ($rejected -and (Hash $unsupported) -eq $futureHash) 'future session recovery retains original'

    $oldConfiguration = Join-Path $run 'legacy.json'
    Copy-Item -LiteralPath (Join-Path $fixtures 'configuration-legacy.json') -Destination $oldConfiguration
    $oldConfigurationHash = Hash $oldConfiguration
    $store = [SqlXmlAnalyzer.Core.Services.RuleConfigurationStore]::new($null, $null)
    $loaded = $store.LoadAsync($oldConfiguration, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $converted = $loaded.Document.WithCurrentSchema()
    $saved = $store.SaveAsAsync((Join-Path $run 'converted.json'), $converted, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    Check ($saved.Succeeded -and $converted.SchemaVersion -eq '1' -and $converted.Fingerprint -eq $loaded.Document.Fingerprint) 'configuration conversion keeps effective settings'
    Check ($converted.UnknownRules.Count -eq 1 -and -not $converted.Get('RULE_036_WAIT_STATS').Enabled) 'configuration alias and unknown rule retained'
    Check ((Hash $oldConfiguration) -eq $oldConfigurationHash) 'configuration source retained'
}
finally { [SqlXmlAnalyzer.Logger]::Shutdown() }

$artifacts = foreach ($file in Get-ChildItem -LiteralPath $run -File) {
    [ordered]@{ Path = $file.Name; Bytes = $file.Length; Sha256 = (Hash $file.FullName) }
}
$result = [ordered]@{
    Configuration = $Configuration; GeneratedAtUtc = [DateTime]::UtcNow.ToString('O'); Passed = $true
    Checks = $checks.ToArray(); Commands = $commands.ToArray(); Artifacts = @($artifacts)
    CliSha256 = (Hash $cli); AppSha256 = (Hash (Join-Path $base 'SqlXmlAnalyzer.dll')); CoreSha256 = (Hash (Join-Path $base 'SqlXmlAnalyzer.Core.dll'))
    Scope = 'Actual CLI processes and file conversions using synthetic offline fixtures; no WPF interaction or SQL Server connection.'
}
$result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $run 'verification.json') -Encoding utf8
$result['Run'] = "runs/$runName"
$result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "latest-$Configuration.json") -Encoding utf8
Write-Output "$Configuration compatibility probe passed $($checks.Count) checks. Evidence: $run"
