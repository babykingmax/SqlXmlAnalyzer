param([string]$RepositoryPath = 'E:/SqlXmlAnalyzer')
$ErrorActionPreference = 'Stop'
$evidencePath = Join-Path $PSScriptRoot 'cli-smoke'
if (Test-Path -LiteralPath $evidencePath) { throw 'Evidence directory already exists; copy this script into a new versioned directory to repeat.' }
[IO.Directory]::CreateDirectory($evidencePath) | Out-Null
$original = [Text.UTF8Encoding]::new($false).GetBytes('SELECT * FROM Users WHERE Age + 10 > 50;')
$originalHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($original))
[IO.File]::WriteAllBytes((Join-Path $evidencePath 'original.sql'), $original)
$results = @()
Push-Location -LiteralPath $RepositoryPath
try {
    foreach ($scenario in @('success', 'locked', 'same-report-path')) {
        $source = Join-Path $evidencePath "$scenario.sql"
        [IO.File]::WriteAllBytes($source, $original)
        $arguments = @('run', '--project', 'SqlXmlAnalyzer.CLI', '--no-build', '--no-restore', '--', 'refactor', $source, '--format', 'json')
        if ($scenario -eq 'same-report-path') { $arguments += @('--output', $source) }
        $handle = $null
        try {
            if ($scenario -eq 'locked') { $handle = [IO.FileStream]::new($source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read) }
            & dotnet @arguments 1> (Join-Path $evidencePath "$scenario.stdout.txt") 2> (Join-Path $evidencePath "$scenario.stderr.txt")
            $exitCode = $LASTEXITCODE
        }
        finally { if ($null -ne $handle) { $handle.Dispose() } }
        $afterHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $backups = @([IO.Directory]::GetFiles($evidencePath, "$scenario.sql.SqlXmlAnalyzer-*.bak", [IO.SearchOption]::TopDirectoryOnly))
        $backupRecords = @($backups | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
        $json = if ($scenario -ne 'same-report-path') { Get-Content -LiteralPath (Join-Path $evidencePath "$scenario.stdout.txt") -Raw | ConvertFrom-Json } else { $null }
        $passed = switch ($scenario) {
            'success' { $exitCode -eq 0 -and $json.IsSuccess -eq $true -and $afterHash -ne $originalHash -and $backups.Count -eq 1 -and $backupRecords[0].sha256 -eq $originalHash }
            'locked' { $exitCode -eq 1 -and $json.IsSuccess -eq $false -and $json.Writeback.Stage -eq 'Replace' -and $json.Writeback.BackupVerified -eq $true -and $json.Writeback.SourceWritten -eq $false -and $json.Writeback.CommitOutcomeUnknown -eq $false -and $afterHash -eq $originalHash -and $backups.Count -eq 1 -and $backupRecords[0].sha256 -eq $originalHash }
            'same-report-path' { $exitCode -eq 2 -and $afterHash -eq $originalHash -and $backups.Count -eq 0 }
        }
        $results += [ordered]@{ scenario = $scenario; executable = 'dotnet'; arguments = $arguments; exitCode = $exitCode; passed = [bool]$passed; sourcePath = $source; originalSha256 = $originalHash; afterSha256 = $afterHash; backups = $backupRecords }
    }
}
finally { Pop-Location }
$report = [ordered]@{ timestampUtc = [DateTime]::UtcNow.ToString('o'); sqlServerExecutionPerformed = $false; total = $results.Count; passed = @($results | Where-Object passed).Count; results = $results }
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'cli-smoke-results.json') -Encoding utf8
$report | ConvertTo-Json -Depth 10
if ($report.passed -ne $report.total) { exit 1 }
