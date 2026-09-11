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
    $trxPath = Join-Path $PSScriptRoot "full-$configuration.trx"
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.failed -ne 0 -or [int]$counters.passed -ne [int]$counters.total) { throw "$configuration tests did not all pass" }
    $buildLog = Join-Path $PSScriptRoot "build-$configuration.log"
    $buildText = Get-Content -LiteralPath $buildLog -Raw
    if ($buildText -notmatch '0\s*(个警告|Warning\(s\))' -or $buildText -notmatch '0\s*(个错误|Error\(s\))') { throw "$configuration build is not clean" }
    $wpfPath = Join-Path $PSScriptRoot "wpf-$configuration/verification.json"
    $wpf = Get-Content -LiteralPath $wpfPath -Raw | ConvertFrom-Json
    if (-not $wpf.Passed -or $wpf.BindingErrors -ne 0 -or $wpf.Matrix.Count -ne 54) { throw "$configuration WPF verification failed" }
    $configurations += [ordered]@{
        configuration = $configuration
        tests = @{ passed = [int]$counters.passed; failed = [int]$counters.failed; skipped = [int]$counters.notExecuted }
        build = @{ warnings = 0; errors = 0; logSha256 = (Get-FileHash -LiteralPath $buildLog).Hash }
        trx = @{ path = "full-$configuration.trx"; sha256 = (Get-FileHash -LiteralPath $trxPath).Hash }
        wpf = @{ path = "wpf-$configuration/verification.json"; combinations = $wpf.Matrix.Count; bindingErrors = $wpf.BindingErrors; sha256 = (Get-FileHash -LiteralPath $wpfPath).Hash }
    }
}
$sources | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $sourceFile -Encoding UTF8
$artifacts = @()
foreach ($configuration in @('Debug', 'Release')) {
    # Non-recursive enumeration only inside this task's evidence directory.
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "wpf-$configuration") -File) {
        if ($file.Extension -notin @('.png', '.json', '.html', '.sqlplan')) { continue }
        $artifacts += @{ path = "wpf-$configuration/$($file.Name)"; sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash }
    }
}
[ordered]@{
    task = 'IMP-25 layout, theme and keyboard operation'
    capturedAtUtc = [DateTime]::UtcNow.ToString('o')
    gitHead = (git -C $repository rev-parse HEAD)
    workspace = 'Current uncommitted workspace; includes prerequisite IMP-23/IMP-24 work.'
    configurations = $configurations
    newTests = 26
    sourceInputs = @{ count = $sources.Count; path = 'source-inputs.json'; sha256 = (Get-FileHash -LiteralPath $sourceFile).Hash }
    tooling = @('WpfProbe.cs.txt', 'Run-WpfProbe.ps1', 'Write-Validation.ps1') | ForEach-Object {
        @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_)).Hash }
    }
    artifacts = $artifacts
    diagnosticEvidence = 'Unit tests validate an actual Windows MiniDump, failed dump creation, deduplication, Debug four-level logging and Release Error/Critical-only logging.'
    manualAcceptance = 'Pending: physical desktop keyboard and native file dialogs, actual monitor DPI switching/multiple monitors, screen-reader operation. Off-screen production WPF layout/commands do not substitute for these checks.'
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding UTF8
Write-Output "Wrote IMP-25 validation for $($sources.Count) source inputs."
