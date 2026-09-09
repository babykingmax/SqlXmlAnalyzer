$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
Push-Location $repository
try {
    $results = foreach ($mode in @('Debug', 'Release')) {
        $stdout = Join-Path $repository ".tmp.imp16-cli-$($mode.ToLowerInvariant()).json"
        $stderr = Join-Path $repository ".tmp.imp16-cli-$($mode.ToLowerInvariant()).stderr"
        dotnet run --project SqlXmlAnalyzer.CLI -c $mode --no-build -- scan --path SqlXmlAnalyzer.Tests/TestData/imp15_index_targets.sqlplan --format json > $stdout 2> $stderr
        if ($LASTEXITCODE -ne 0) { throw "$mode CLI failed" }
        $result = @(Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json)[0]
        if ($result.Diagnostics.HasFailures) { throw "$mode diagnostics contain failures" }
        $candidates = @($result.Diagnostics.Diagnostics | Where-Object SemanticCode -eq 'INDEX_SQL_CANDIDATE')
        if ($candidates.Count -ne 4) { throw "$mode expected four statement candidates" }
        foreach ($candidate in $candidates) {
            if ($candidate.RuleVersion -ne '2.1.0') { throw 'Wrong rule version' }
            if (@($candidate.Evidence | Where-Object { $_.Name -eq 'ScoreModel' -and $_.Value -eq 'index-evidence-score/2.0.0' }).Count -ne 1) { throw 'Missing model' }
            if (@($candidate.Evidence | Where-Object Name -in @('ScoreWeights', 'ScoreInputScope', 'ScoreEvidenceSource')).Count -ne 3) { throw 'Missing assumptions' }
        }
        $log = [IO.File]::ReadAllText($stderr)
        if ($mode -eq 'Release' -and $log -match '\[(DEBUG|WARN|INFO)\]') { throw 'Release log level leak' }
        if ($mode -eq 'Debug' -and $log -notmatch '\[DEBUG\]') { throw 'Debug diagnostic log missing' }
        [ordered]@{
            Configuration = $mode; ExitCode = 0; HasFailures = $false; CandidateCount = $candidates.Count
            StdoutJsonValid = $true; StdoutSha256 = (Get-FileHash -LiteralPath $stdout).Hash; LogPolicyPassed = $true
            Candidates = @($candidates | ForEach-Object { [ordered]@{
                RuleId = $_.RuleId; RuleVersion = $_.RuleVersion; Scope = $_.Location.DisplayScope
                Evidence = @($_.Evidence | Where-Object Name -like 'Score*' | Select-Object Name,Value,Source)
                Limitations = $_.Limitations
            } })
        }
    }
    $results | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $PSScriptRoot 'cli-validation.json') -Encoding utf8
    Write-Output 'PASS: Debug/Release CLI JSON, four candidates, rule/model versions, evidence scope/weights and log filtering.'
} finally { Pop-Location }
