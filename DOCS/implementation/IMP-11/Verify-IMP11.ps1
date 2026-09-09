param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../..'))
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$results = @()
Push-Location $repository
try {
    foreach ($configuration in @('Debug', 'Release')) {
        $name = $configuration.ToLowerInvariant()
        dotnet build SqlXmlAnalyzer.sln -c $configuration -v minimal *> (Join-Path $PSScriptRoot "$name-build.log")
        $buildExit = $LASTEXITCODE
        if ($buildExit -ne 0) { throw "$configuration build failed; see $name-build.log" }
        dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c $configuration --no-build --no-restore -v minimal --logger "trx;LogFileName=$name.trx" --results-directory $PSScriptRoot *> (Join-Path $PSScriptRoot "$name-test.log")
        $testExit = $LASTEXITCODE
        [xml]$trx = Get-Content -LiteralPath (Join-Path $PSScriptRoot "$name.trx") -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        $results += [ordered]@{Configuration=$configuration; BuildExit=$buildExit; TestExit=$testExit; Total=[int]$counters.total; Passed=[int]$counters.passed; Failed=[int]$counters.failed; Trx="$name.trx"}
        if ($testExit -ne 0) { throw "$configuration tests failed; see $name-test.log" }
    }
    [ordered]@{TimestampUtc=[DateTime]::UtcNow.ToString('O'); Configurations=$results;
        CoreAssemblySha256=(Get-FileHash SqlXmlAnalyzer.Core/bin/Release/net8.0/SqlXmlAnalyzer.Core.dll).Hash;
        FactsSourceSha256=(Get-FileHash SqlXmlAnalyzer.Core/Services/PlanOperatorFactsService.cs).Hash} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
    $results | ConvertTo-Json
}
finally { Pop-Location }
