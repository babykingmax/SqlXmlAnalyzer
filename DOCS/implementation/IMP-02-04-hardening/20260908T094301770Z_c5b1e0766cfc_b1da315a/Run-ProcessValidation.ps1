param([string]$RepositoryPath = 'E:/SqlXmlAnalyzer')
$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'process-final-02'
if (Test-Path -LiteralPath $destination) { throw 'Evidence already exists. Use a new versioned directory.' }
[IO.Directory]::CreateDirectory($destination) | Out-Null
$temporaryProject = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-Imp04Probe-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryProject) | Out-Null
$commands = [Collections.Generic.List[object]]::new()
Import-Module (Join-Path $RepositoryPath 'DOCS/acceptance/IMP-02/AcceptanceInfrastructure.psm1') -Force
function Invoke-Dotnet([string[]]$Arguments, [string]$Stem) {
    $record=Invoke-AcceptanceProcess 'dotnet' $Arguments $RepositoryPath $Stem 60000
    $record | Add-Member -NotePropertyName startUtc -NotePropertyValue $record.startedUtc
    $commands.Add($record)
    if ($record.timedOut -or -not $record.outputComplete -or $null -ne $record.failure) { throw "Process failed: $($record.failure)" }
    return $record
}
$results = @()
try {
    $projectXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="' + [Security.SecurityElement]::Escape((Join-Path $RepositoryPath 'SqlXmlAnalyzer.CLI/SqlXmlAnalyzer.CLI.csproj')) + '" /></ItemGroup></Project>'
    $projectPath = Join-Path $temporaryProject 'DiagnosticProbe.csproj'
    [IO.File]::WriteAllText($projectPath, $projectXml)
    [IO.File]::WriteAllText((Join-Path $destination 'DiagnosticProbe.csproj.snapshot'), $projectXml)
    [IO.File]::Copy((Join-Path $PSScriptRoot 'DiagnosticProbe.cs.txt'), (Join-Path $temporaryProject 'Program.cs'))
    $restore = Invoke-Dotnet @('restore', $projectPath, '--nologo') (Join-Path $destination 'probe-restore')
    if ($restore.exitCode -ne 0) { throw 'Probe restore failed' }
    foreach ($configuration in @('Debug', 'Release')) {
        $configurationPath = Join-Path $destination $configuration.ToLowerInvariant()
        [IO.Directory]::CreateDirectory($configurationPath) | Out-Null
        $build = Invoke-Dotnet @('build', $projectPath, '-c', $configuration, '--no-restore', '--nologo', '-p:BuildProjectReferences=false') (Join-Path $configurationPath 'probe-build')
        if ($build.exitCode -ne 0) { throw "Probe $configuration build failed" }
        foreach ($faultScenario in @('unknown-error','unknown-writeback')) {
        $unknownPath = Join-Path $configurationPath $faultScenario
        $unknown = Invoke-Dotnet @((Join-Path $temporaryProject "bin/$configuration/net8.0/DiagnosticProbe.dll"), $unknownPath, $faultScenario) (Join-Path $configurationPath $faultScenario)
        $verification = Get-Content -LiteralPath (Join-Path $unknownPath 'verification.json') -Raw | ConvertFrom-Json
        $log = Get-Content -LiteralPath (Join-Path $unknownPath 'runtime.log') -Raw
        $policyPassed = $log.Contains('probe-error-marker') -and $log.Contains('probe-fatal-marker') -and
            $(if ($configuration -eq 'Debug') { $log.Contains('probe-debug-marker') -and $log.Contains('probe-warning-marker') } else { -not $log.Contains('probe-debug-marker') -and -not $log.Contains('probe-warning-marker') })
        $results += [ordered]@{ configuration = $configuration; scenario = $faultScenario; passed = $unknown.exitCode -eq 1 -and $verification.Passed -and $verification.Configuration -eq $configuration -and $policyPassed; logPolicyPassed = $policyPassed; verification = $verification }
        }
        foreach ($scenario in @('risk-preserved', 'candidate', 'applied')) {
            $sql = if ($scenario -eq 'risk-preserved') { "DECLARE @stage TABLE(Id int); SELECT LTRIM(Name) FROM Users;" } else { 'SELECT * FROM Users WHERE Age + 10 > 50;' }
            $source = Join-Path $configurationPath "$scenario.sql"
            [IO.File]::WriteAllText($source, $sql)
            [IO.File]::WriteAllText((Join-Path $configurationPath "$scenario.original.sql"), $sql)
            $before = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
            $cliArgs = @((Join-Path $RepositoryPath "SqlXmlAnalyzer.CLI/bin/$configuration/net8.0/SqlXmlAnalyzer.CLI.dll"), 'refactor', $source, '--format', 'json', '--show-sql')
            if ($scenario -eq 'candidate') { $cliArgs += '--dry-run' }
            $stem = Join-Path $configurationPath $scenario
            $command = Invoke-Dotnet $cliArgs $stem
            $json = Get-Content -LiteralPath "$stem.stdout.txt" -Raw | ConvertFrom-Json
            $after = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
            $expectedOutcome = switch ($scenario) { 'risk-preserved' { 'NoChanges' }; 'candidate' { 'CandidateGenerated' }; 'applied' { 'Applied' } }
            $sourcePassed = if ($scenario -eq 'applied') { $after -ne $before -and $json.SourceWritten } else { $after -eq $before -and -not $json.SourceWritten }
            $skipPassed = $scenario -ne 'risk-preserved' -or ($json.SafetySkips.Count -eq 2 -and $json.Warnings.Count -eq 2 -and -not $json.HasChanges -and $json.Warnings[0].Contains('已跳过') -and $json.SafetySkips[0].RequiredEvidence.Contains('回滚') -and -not ($json | ConvertTo-Json -Depth 10).Contains([char]0xFFFD))
            $runtimeLogDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SqlXmlAnalyzer/log'
            $runtimeLogs = @([IO.Directory]::GetFiles($runtimeLogDirectory, "*_$($command.processId)_*.log", [IO.SearchOption]::TopDirectoryOnly) | Where-Object { [IO.File]::GetLastWriteTimeUtc($_) -ge [DateTime]::Parse($command.startUtc).ToUniversalTime() })
            if ($runtimeLogs.Count -ne 1) { throw 'Cannot identify the CLI process log unambiguously.' }
            [IO.File]::Copy($runtimeLogs[0], "$stem.runtime.log")
            $runtimeLog = [IO.File]::ReadAllText("$stem.runtime.log")
            $cliLogPassed = if ($configuration -eq 'Release') { $runtimeLog -notmatch '\[(DEBUG|WARN|INFO|VERBOSE)\]' } else { $runtimeLog.Contains('[DEBUG]') -and ($scenario -ne 'risk-preserved' -or $runtimeLog.Contains('[WARN]')) }
            $results += [ordered]@{ configuration = $configuration; scenario = $scenario; passed = $command.exitCode -eq 0 -and $json.IsSuccess -and $json.Outcome -eq $expectedOutcome -and $sourcePassed -and $skipPassed -and $cliLogPassed; outcome = $json.Outcome; sourceBeforeSha256 = $before; sourceAfterSha256 = $after; logPolicyPassed = $cliLogPassed; backupPaths = @([IO.Directory]::GetFiles($configurationPath, "$scenario.sql.SqlXmlAnalyzer-*.bak", [IO.SearchOption]::TopDirectoryOnly)) }
        }
    }
}
finally {
    ConvertTo-Json -InputObject @($commands) -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'commands.json') -Encoding utf8
    $resolved = [IO.Path]::GetFullPath($temporaryProject)
    $prefix = Join-Path ([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) 'SqlXmlAnalyzer-Imp04Probe-'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$summary = [ordered]@{ total = $results.Count; passed = @($results | Where-Object passed).Count; results = $results; sqlServerExecutionPerformed = $false; wpfVisualTestPerformed = $false }
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 12
if ($summary.passed -ne $summary.total) { exit 1 }
