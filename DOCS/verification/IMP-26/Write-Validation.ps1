param([switch]$VerifySources)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$paths = git -C $repository ls-files --cached --others --exclude-standard -- '*.cs' '*.xaml' '*.csproj' '*.props' '*.targets' '*.sln' 'RuleConfiguration.json'
if ($LASTEXITCODE -ne 0) { throw 'Source inventory failed' }
$paths = $paths | Where-Object { $_ -notmatch '(^|/)(\.git|bin|obj|\.vs|publish[^/]*|backups|\.tmp[^/]*)/' -and $_ -notmatch '(_wpftmp|\.tmp\.)' } | Sort-Object -Unique
$sources = @($paths | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $repository $_) -Algorithm SHA256).Hash } })
$sourceFile = Join-Path $PSScriptRoot 'source-inputs.json'
if ($VerifySources) {
    $expected = Get-Content -LiteralPath $sourceFile -Raw | ConvertFrom-Json
    $before = @($expected | ForEach-Object { "$($_.path):$($_.sha256)" })
    $after = @($sources | ForEach-Object { "$($_.path):$($_.sha256)" })
    if (Compare-Object $before $after) { throw 'Source inputs changed after validation snapshot' }
    Write-Output "Verified $($sources.Count) source inputs are unchanged."
    exit 0
}
$configurations = @()
foreach ($configuration in @('Debug', 'Release')) {
    $trxPath = Join-Path $repository ".tmp.imp26-results/IMP26-$configuration.trx"
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.failed -ne 0 -or [int]$counters.passed -ne [int]$counters.total) { throw "$configuration tests did not all pass" }
    $buildLog = Join-Path $PSScriptRoot "build-$configuration.log"
    $buildText = Get-Content -LiteralPath $buildLog -Raw
    if ($buildText -notmatch '0\s*(个警告|Warning\(s\))' -or $buildText -notmatch '0\s*(个错误|Error\(s\))') { throw "$configuration build is not clean" }
    $wpfPath = Join-Path $PSScriptRoot "wpf-$configuration/verification.json"
    $wpf = Get-Content -LiteralPath $wpfPath -Raw | ConvertFrom-Json
    if (-not $wpf.Passed -or $wpf.BindingErrors -ne 0 -or $wpf.Matrix.Count -ne 54 -or
        $wpf.Checks -notcontains 'F6 skips disabled A/B actions and reaches plan A' -or
        $wpf.Checks -notcontains 'AutomationInvoke Escape returns to the same graph node' -or
        $wpf.Checks -notcontains 'Cancellation after eight committed graph nodes removes incomplete presentation and never reports Ready' -or
        $wpf.Checks -notcontains 'Analysis state contrast >= 4.5:1 (dark=False)' -or
        $wpf.Checks -notcontains 'Analysis state contrast >= 4.5:1 (dark=True)') { throw "$configuration WPF verification failed" }
    $configurations += [ordered]@{
        configuration = $configuration
        tests = @{ passed = [int]$counters.passed; failed = [int]$counters.failed; skipped = [int]$counters.notExecuted }
        build = @{ warnings = 0; errors = 0; logSha256 = (Get-FileHash -LiteralPath $buildLog).Hash }
        trx = @{ path = ".tmp.imp26-results/IMP26-$configuration.trx"; sha256 = (Get-FileHash -LiteralPath $trxPath).Hash }
        wpf = @{ path = "wpf-$configuration/verification.json"; combinations = $wpf.Matrix.Count; bindingErrors = $wpf.BindingErrors; sha256 = (Get-FileHash -LiteralPath $wpfPath).Hash }
    }
}
$sources | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $sourceFile -Encoding UTF8
$performance = @()
$table = @('| 输入 | 运行 | 总耗时 ms（前 → 后） | 输入 P95 ms（前 → 后） | 峰值 MiB（前 → 后） |', '| --- | --- | ---: | ---: | ---: |')
foreach ($fixture in 'plan-100','plan-1000','plan-5000','xel-multiple') {
    $before = Get-Content -LiteralPath (Join-Path $PSScriptRoot "before/$fixture.json") -Raw | ConvertFrom-Json
    $after = Get-Content -LiteralPath (Join-Path $PSScriptRoot "after/$fixture.json") -Raw | ConvertFrom-Json
    if (-not $before.Passed -or -not $after.Passed -or $before.SourceSha256 -ne $after.SourceSha256 -or $before.Runs.Count -ne 2 -or $after.Runs.Count -ne 2) { throw "$fixture measurement missing, failed or source hash changed" }
    for ($i=0; $i -lt 2; $i++) {
        $a=$before.Runs[$i]; $b=$after.Runs[$i]
        $table += '| {0} | {1} | {2:F2} → {3:F2} | {4:F2} → {5:F2} | {6:F1} → {7:F1} |' -f $fixture,$a.Run,$a.TotalMs,$b.TotalMs,$a.InputLatencyP95Ms,$b.InputLatencyP95Ms,($a.PeakWorkingSetBytes/1MB),($b.PeakWorkingSetBytes/1MB)
    }
    $performance += @{ fixture=$fixture; inputSha256=$after.SourceSha256; beforeSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot "before/$fixture.json")).Hash; afterSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot "after/$fixture.json")).Hash }
}
$readme = Join-Path $PSScriptRoot 'README.md'
$text = Get-Content -LiteralPath $readme -Raw
$replacement = '<!-- PERF_TABLE_START -->' + [Environment]::NewLine + ($table -join [Environment]::NewLine) + [Environment]::NewLine + '<!-- PERF_TABLE_END -->'
$text = [regex]::Replace($text, '<!-- PERF_TABLE_START -->[\s\S]*?<!-- PERF_TABLE_END -->', [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $replacement })
Set-Content -LiteralPath $readme -Value $text -Encoding UTF8
$artifacts = @()
foreach ($configuration in @('Debug', 'Release')) {
    # Non-recursive enumeration only inside this task's evidence directory.
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "wpf-$configuration") -File) {
        if ($file.Extension -notin @('.png', '.json', '.html', '.sqlplan')) { continue }
        $artifacts += @{ path = "wpf-$configuration/$($file.Name)"; sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash }
    }
}
[ordered]@{
    task = 'IMP-26 async operation states, cancellation and bounded large graph presentation'
    capturedAtUtc = [DateTime]::UtcNow.ToString('o')
    gitHead = (git -C $repository rev-parse HEAD)
    workspace = 'Current uncommitted workspace; includes prerequisite IMP-23/IMP-24/IMP-25 work.'
    configurations = $configurations
    performance = $performance
    newTests = 32
    sourceInputs = @{ count = $sources.Count; path = 'source-inputs.json'; sha256 = (Get-FileHash -LiteralPath $sourceFile).Hash }
    tooling = @('WpfProbe.cs.txt', 'Run-WpfProbe.ps1', 'Write-Validation.ps1', 'PerformanceProbe.cs.txt', 'Run-Performance.ps1', 'Capture-XelFixture.ps1', 'Run-XelPerformance.ps1', 'Build-BaselineProbe.ps1') | ForEach-Object {
        @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_)).Hash }
    }
    artifacts = $artifacts
    diagnosticEvidence = 'Unit tests validate an actual Windows MiniDump, failed dump creation, deduplication, Debug four-level logging and Release Error/Critical-only logging.'
    manualAcceptance = 'Pending: physical desktop keyboard and native file dialogs, actual monitor DPI switching/multiple monitors, screen-reader operation. Off-screen production WPF layout/commands do not substitute for these checks.'
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding UTF8
Write-Output "Wrote IMP-26 validation for $($sources.Count) source inputs."
