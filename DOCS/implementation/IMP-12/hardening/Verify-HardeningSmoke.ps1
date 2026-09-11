param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../../..'))
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$cli = Join-Path $repository 'SqlXmlAnalyzer.CLI/bin/Release/net8.0/SqlXmlAnalyzer.CLI.dll'
$results = @()
Push-Location $repository
try {
    foreach ($name in @('serial-clean', 'parallel-branch')) {
        $inputPath = Join-Path $PSScriptRoot "$name.sqlplan"
        $outputPath = Join-Path $PSScriptRoot "$name.json"
        dotnet $cli --path $inputPath --format json --output $outputPath *> (Join-Path $PSScriptRoot "$name-cli.log")
        $scanExit = $LASTEXITCODE
        $scan = @(Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json)[0]
        if ($scanExit -ne 0 -or $scan.Status -ne 'Passed' -or $scan.Diagnostics.HasFailures) { throw "$name scan failed" }
        if ($name -eq 'serial-clean' -and ($scan.Diagnostics.HasMissingEvidence -or $scan.Diagnostics.HitCount -ne 0)) { throw 'Clean serial plan has false diagnostics' }
        if ($name -eq 'parallel-branch' -and -not @($scan.Diagnostics.Diagnostics | Where-Object RuleId -eq 'RULE_010_INEFFECTIVE_PARALLELISM')) { throw 'Parallel result branch was lost' }
        $results += [ordered]@{Name=$name;ExitCode=$scanExit;Status=$scan.Status;Hit=$scan.Diagnostics.HitCount;
            NoHit=$scan.Diagnostics.NoHitCount;Skipped=$scan.Diagnostics.SkippedCount;Failed=$scan.Diagnostics.FailedCount;
            HasMissingEvidence=$scan.Diagnostics.HasMissingEvidence;Capabilities=$scan.Diagnostics.Capabilities;
            ResultRuleIds=@($scan.Diagnostics.Diagnostics | ForEach-Object RuleId)}
    }
    $junitPath = Join-Path $PSScriptRoot 'parallel-branch.junit.xml'
    dotnet $cli --path (Join-Path $PSScriptRoot 'parallel-branch.sqlplan') --format junit --output $junitPath *> (Join-Path $PSScriptRoot 'parallel-junit-cli.log')
    if ($LASTEXITCODE -ne 0) { throw 'JUnit scan failed' }
    [xml]$junit = Get-Content -LiteralPath $junitPath -Raw
    if ($junit.testsuites.testsuite.failures -ne '0' -or $junit.SelectSingleNode('//system-out').InnerText -notlike '*RULE_010_INEFFECTIVE_PARALLELISM*') { throw 'JUnit lost successful branch diagnostic' }
    [ordered]@{TimestampUtc=[DateTime]::UtcNow.ToString('O');CliAssemblySha256=(Get-FileHash -LiteralPath $cli).Hash;
        Cases=$results;JUnitFailures=$junit.testsuites.testsuite.failures} | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $PSScriptRoot 'smoke-verification.json') -Encoding utf8
    $results | ConvertTo-Json -Depth 5
}
finally { Pop-Location }
