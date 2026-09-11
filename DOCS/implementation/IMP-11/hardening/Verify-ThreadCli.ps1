param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../../..'))
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
Push-Location $repository
try {
    $cli = Join-Path $repository 'SqlXmlAnalyzer.CLI/bin/Release/net8.0/SqlXmlAnalyzer.CLI.dll'
    $fixture = Join-Path $PSScriptRoot 'signed-thread.sqlplan'
    $readPath = Join-Path $PSScriptRoot 'cli-read.json'
    $scanPath = Join-Path $PSScriptRoot 'cli-scan.json'
    dotnet $cli read $fixture > $readPath
    $readExit = $LASTEXITCODE
    dotnet $cli --path $fixture --format json > $scanPath
    $scanExit = $LASTEXITCODE
    $read = Get-Content -LiteralPath $readPath -Raw | ConvertFrom-Json
    $scan = @(Get-Content -LiteralPath $scanPath -Raw | ConvertFrom-Json)[0]
    $readFacts = $read.Plan.Batches[0].Statements[0].QueryPlans[0].Operators[0].Facts
    $scanFacts = $scan.Plan.Batches[0].Statements[0].QueryPlans[0].Operators[0].Facts
    $criticalRules = @($scan.Issues | Where-Object Severity -eq 'Critical' | ForEach-Object RuleId)
    if ($readExit -ne 0 -or $scanExit -ne 1 -or $read.Status -ne 'Success' -or $scan.InputStatus -ne 'Success' -or
        $criticalRules -notcontains 'RULE_004_ESTIMATE_MISMATCH' -or $criticalRules -notcontains 'RULE_030_CARDINALITY_ERROR') {
        throw 'Signed-thread CLI did not produce the expected cardinality diagnostics and exit codes.'
    }
    foreach ($facts in @($readFacts, $scanFacts)) {
        if ($facts.Threads[0].ThreadId -ne 0 -or $facts.Threads[0].SourceAttributes.Thread -ne '+0' -or
            $facts.OutputRows.Value -ne 20000 -or $facts.LogicalExecutions.Value -ne 1 -or
            $facts.RowsPerExecution.Value -ne 20000 -or $facts.RowsPerExecution.State -ne 'Available') {
            throw 'Signed-thread CLI facts do not match the metric contract.'
        }
    }
    $observed = [ordered]@{
        Passed = $true; ReadExit = $readExit; ScanExit = $scanExit; InputStatus = $scan.InputStatus
        ScanExitReason = $scan.FailureMessage; CriticalRules = $criticalRules
        RawThread = $readFacts.Threads[0].SourceAttributes.Thread; ThreadId = $readFacts.Threads[0].ThreadId
        OutputRows = $readFacts.OutputRows.Value; LogicalExecutions = $readFacts.LogicalExecutions.Value
        RowsPerExecution = $readFacts.RowsPerExecution.Value; ReadAndScanAgree = $true
        CoreAssemblySha256 = (Get-FileHash 'SqlXmlAnalyzer.Core/bin/Release/net8.0/SqlXmlAnalyzer.Core.dll').Hash
    }
    $observed | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'cli-observed.json') -Encoding utf8
    $observed | ConvertTo-Json
}
finally { Pop-Location }
