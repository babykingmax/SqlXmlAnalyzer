$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$future = Join-Path $PSScriptRoot 'cli-future.json'
[IO.File]::WriteAllText($future, '{"futureRoot":{"keep":true},"Rules":[{"RuleId":"FUTURE_RULE","Enabled":"future-mode"},{"RuleId":"RULE_004_ESTIMATE_MISMATCH","Enabled":false},{"RuleId":"RULE_030_CARDINALITY_ERROR","Enabled":false}]}')
$outcomes = @()
foreach ($configuration in @('Debug','Release')) {
    $cli = Join-Path $repository "SqlXmlAnalyzer.CLI/bin/$configuration/net8.0/SqlXmlAnalyzer.CLI.exe"
    foreach ($kind in @('legacy','future')) {
        $configPath = if ($kind -eq 'legacy') { Join-Path $repository 'RuleConfiguration.json' } else { $future }
        $output = Join-Path $PSScriptRoot "cli-$configuration-$kind.json"
        $errorLog = Join-Path $PSScriptRoot "cli-$configuration-$kind.log"
        & $cli --path (Join-Path $PSScriptRoot 'wpf-Debug/fixture.sqlplan') --config $configPath --format json 1>$output 2>$errorLog
        $code = $LASTEXITCODE
        $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        $hits = @($result.Diagnostics.Diagnostics | Where-Object { $_.RuleId -in @('RULE_004_ESTIMATE_MISMATCH','RULE_030_CARDINALITY_ERROR') }).Count
        $expectedCode = if ($kind -eq 'legacy') { 1 } else { 0 }
        $expectedHits = if ($kind -eq 'legacy') { 1 } else { 0 }
        if ($code -ne $expectedCode -or $result.InputStatus -ne 'Success' -or $hits -ne $expectedHits) {
            throw "Unexpected CLI result: $configuration $kind, code=$code, hits=$hits"
        }
        $outcomes += [ordered]@{ configuration=$configuration; kind=$kind; exitCode=$code; inputStatus=$result.InputStatus; matchingDiagnosticCount=$hits; passed=$true }
    }
}
$outcomes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'cli-verification.json') -Encoding utf8
$outcomes | ConvertTo-Json -Compress
