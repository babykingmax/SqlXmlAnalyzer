$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
Push-Location $repository
try {
    $configurations = @()
    foreach ($configuration in @('Debug', 'Release')) {
        $trxPath = Join-Path $repository "SqlXmlAnalyzer.Tests/TestResults/imp24-full-$configuration.trx"
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $reader = [Xml.XmlReader]::Create($trxPath, $settings)
        try { $trx = [Xml.XmlDocument]::new(); $trx.XmlResolver = $null; $trx.Load($reader) } finally { $reader.Dispose() }
        $counters = $trx.TestRun.ResultSummary.Counters
        $focused = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match 'RuleConfiguration' } | ForEach-Object {
            [ordered]@{ name = $_.testName; outcome = $_.outcome; duration = $_.duration }
        })
        $wpf = Get-Content -LiteralPath (Join-Path $PSScriptRoot "wpf-$configuration/verification.json") -Raw | ConvertFrom-Json
        if ([int]$counters.failed -ne 0 -or [int]$counters.passed -ne 2527 -or $focused.Count -ne 54 -or !$wpf.Passed) {
            throw "Unexpected verification result for $configuration"
        }
        $configurations += [ordered]@{
            configuration = $configuration
            counters = [ordered]@{ total = [int]$counters.total; passed = [int]$counters.passed; failed = [int]$counters.failed; skipped = [int]$counters.notExecuted }
            trx = [ordered]@{ path = "SqlXmlAnalyzer.Tests/TestResults/imp24-full-$configuration.trx"; sha256 = (Get-FileHash -LiteralPath $trxPath -Algorithm SHA256).Hash }
            focusedTests = $focused
            wpf = $wpf
        }
    }
    $paths = @(git -c core.quotePath=false ls-files --cached --others --exclude-standard '*.cs' '*.xaml' '*.csproj' '*.props' '*.targets' '*.sln' '*.json') |
        Where-Object { $_ -notmatch '(^|/)(DOCS|\.git|bin|obj|\.vs|backups|publish[^/]*|\.tmp\.[^/]*)(/|$)' } | Sort-Object -Unique
    $sources = @($paths | ForEach-Object {
        $path = Join-Path $repository $_
        [ordered]@{ path = $_; bytes = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    })
    $sources | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'source-inputs.json') -Encoding UTF8
    $artifacts = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Name -notin @('summary.json','source-inputs.json') } |
        ForEach-Object { [ordered]@{ path = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    foreach ($configuration in @('Debug','Release')) {
        $artifacts += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "wpf-$configuration") -File | ForEach-Object {
            [ordered]@{ path = "wpf-$configuration/" + $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    }
    [ordered]@{
        task = 'IMP-24'; capturedAtUtc = [DateTime]::UtcNow.ToString('O'); gitHead = (git rev-parse HEAD)
        workspace = 'Current uncommitted workspace, includes prior IMP-23 changes; not a published revision'
        configurations = $configurations
        sourceFiles = $sources.Count; sourceManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'source-inputs.json') -Algorithm SHA256).Hash
        artifacts = $artifacts
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding UTF8
    Write-Output "Evidence collected: $($sources.Count) source/project/configuration files."
} finally { Pop-Location }
