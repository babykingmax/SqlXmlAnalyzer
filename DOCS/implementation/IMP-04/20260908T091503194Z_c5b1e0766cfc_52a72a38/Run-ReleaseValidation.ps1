param([string]$RepositoryPath = 'E:/SqlXmlAnalyzer')
$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'release-final-02'
if (Test-Path -LiteralPath $destination) { throw 'Evidence already exists. Use a new versioned directory.' }
[IO.Directory]::CreateDirectory($destination) | Out-Null
Import-Module (Join-Path $RepositoryPath 'DOCS/acceptance/IMP-02/AcceptanceInfrastructure.psm1') -Force
$before = Get-AcceptanceInputSnapshot -RepositoryPath $RepositoryPath
$before | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'input-before.json') -Encoding utf8
$commands = @()
Push-Location -LiteralPath $RepositoryPath
try {
    foreach ($step in @('build', 'test')) {
        $arguments = if ($step -eq 'build') { @('build', 'SqlXmlAnalyzer.sln', '--configuration', 'Release', '--no-incremental', '--no-restore', '--nologo') }
            else { @('test', 'SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj', '--configuration', 'Release', '--no-build', '--no-restore', '--nologo', '--logger', 'trx;LogFileName=full-release.trx', '--results-directory', $destination) }
        $start = [DateTime]::UtcNow
        & dotnet @arguments 1> (Join-Path $destination "$step.stdout.txt") 2> (Join-Path $destination "$step.stderr.txt")
        $code = $LASTEXITCODE
        $commands += [ordered]@{ executable = 'dotnet'; arguments = $arguments; startUtc = $start.ToString('o'); endUtc = [DateTime]::UtcNow.ToString('o'); exitCode = $code }
        Write-Output "$step : exit $code"
        if ($code -ne 0) { throw "$step failed; inspect the retained logs." }
    }
}
finally { Pop-Location; ConvertTo-Json -InputObject $commands -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'commands.json') -Encoding utf8 }
$after = Get-AcceptanceInputSnapshot -RepositoryPath $RepositoryPath
$after | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'input-after.json') -Encoding utf8
$drift = @(Compare-AcceptanceInputSnapshot -Before $before -After $after)
ConvertTo-Json -InputObject $drift -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'protected-input-drift.json') -Encoding utf8
[xml]$trx = Get-Content -LiteralPath (Join-Path $destination 'full-release.trx') -Raw
$counts = $trx.TestRun.ResultSummary.Counters
$summary = [ordered]@{ configuration = 'Release'; head = $after.head; total = [int]$counts.total; passed = [int]$counts.passed; failed = [int]$counts.failed; notExecuted = [int]$counts.notExecuted; inputDrift = $drift; buildExitCode = $commands[0].exitCode; testExitCode = $commands[1].exitCode }
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 6
if ($drift.Count -gt 0 -or $summary.failed -gt 0) { exit 1 }
