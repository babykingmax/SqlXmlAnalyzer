param([switch]$VerifySources)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sourceManifest = Join-Path $PSScriptRoot 'sources.json'
if ($VerifySources) {
    $sources = Get-Content -LiteralPath $sourceManifest -Raw | ConvertFrom-Json
    foreach ($source in $sources) {
        $path = Join-Path $repository $source.Path
        if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $source.Sha256) {
            throw "Source changed after validation: $($source.Path)"
        }
    }
    Write-Output "Verified $($sources.Count) source hashes."
    return
}
$modes = foreach ($configuration in @('Debug', 'Release')) {
    $lower = $configuration.ToLowerInvariant()
    $buildLog = Join-Path $repository ".tmp.imp27-build-$lower.log"
    $buildText = Get-Content -LiteralPath $buildLog -Raw
    if ($buildText -notmatch '0 个警告' -or $buildText -notmatch '0 个错误') { throw "Build not clean: $configuration" }
    $trx = Join-Path $repository ".tmp.imp27-results/$configuration/full.trx"
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($trx, $settings)
    $tests = [Collections.Generic.List[object]]::new()
    $counters = $null
    try {
        while ($reader.Read()) {
            if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
            if ($reader.LocalName -eq 'Counters') {
                $counters = [ordered]@{ Total = [int]$reader.GetAttribute('total'); Passed = [int]$reader.GetAttribute('passed'); Failed = [int]$reader.GetAttribute('failed'); NotExecuted = [int]$reader.GetAttribute('notExecuted') }
            }
            if ($reader.LocalName -eq 'UnitTestResult' -and $reader.GetAttribute('testName').Contains('DiagnosticPackage')) {
                $tests.Add([ordered]@{ Name = $reader.GetAttribute('testName'); Outcome = $reader.GetAttribute('outcome') })
            }
        }
    }
    finally { $reader.Dispose() }
    if ($counters.Total -ne 2684 -or $counters.Passed -ne 2684 -or $counters.Failed -ne 0 -or $tests.Count -ne 39 -or @($tests | Where-Object Outcome -ne 'Passed').Count -ne 0) { throw "Unexpected test results: $configuration" }
    $wpfPath = Join-Path $PSScriptRoot "wpf-$configuration/verification.json"
    $wpf = Get-Content -LiteralPath $wpfPath -Raw | ConvertFrom-Json
    if ($wpf.Configuration -ne $configuration -or $wpf.BindingErrors.Length -ne 0 -or $wpf.Checks.Count -lt 40) { throw "WPF verification incomplete: $configuration" }
    $artifacts = foreach ($name in @('verification.json', 'review-light.png', 'review-dark.png', 'review-compact.png', 'synthetic.sqlplan', 'default-package.zip')) {
        $path = Join-Path $PSScriptRoot "wpf-$configuration/$name"
        [ordered]@{ Path = "wpf-$configuration/$name"; Bytes = (Get-Item -LiteralPath $path).Length; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    [ordered]@{
        Configuration = $configuration; BuildWarnings = 0; BuildErrors = 0
        BuildLog = [ordered]@{ Path = ".tmp.imp27-build-$lower.log"; Sha256 = (Get-FileHash -LiteralPath $buildLog).Hash }
        Tests = $counters; NewTests = $tests.ToArray()
        Trx = [ordered]@{ Path = ".tmp.imp27-results/$configuration/full.trx"; Sha256 = (Get-FileHash -LiteralPath $trx).Hash }
        WpfChecks = $wpf.Checks.Count; WpfBindingErrors = 0; Artifacts = @($artifacts)
    }
}
Push-Location $repository
try {
    $sourcePaths = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.cs' '*.xaml' '*.csproj' '*.props' '*.sln' 'RuleConfiguration.json' |
        Where-Object { $_ -notmatch '(^|/)(\.git|bin|obj|\.vs|publish-[^/]*|backups|\.tmp\.[^/]*)(/|$)' -and $_ -notmatch '^DOCS/' } | Sort-Object -Unique)
    $sourcePaths += @('DOCS/verification/IMP-27/Run-WpfProbe.ps1', 'DOCS/verification/IMP-27/WpfProbe.cs.txt', 'DOCS/verification/IMP-27/Write-Validation.ps1')
    $sources = foreach ($relative in $sourcePaths) {
        $path = Join-Path $repository $relative
        [ordered]@{ Path = $relative; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    $head = git rev-parse HEAD
}
finally { Pop-Location }
$sources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $sourceManifest -Encoding UTF8
[ordered]@{
    Implementation = 'IMP-27'; GeneratedAtUtc = [DateTime]::UtcNow.ToString('O'); GitHead = $head
    Context = 'Current working tree includes preceding uncommitted IMP-23 through IMP-26 work; no commit or publication performed.'
    SourceManifest = 'sources.json'; SourceCount = $sources.Count; CoverageGenerated = $false
    Configurations = @($modes)
    Limitations = @('Offscreen WPF with injected file dialogs; not physical keyboard, screen reader or display DPI acceptance.', 'Checksums prove byte identity, not authenticity or anonymous content.', 'Raw TRX, logs, ZIP and test DUMPs are local-only evidence; DUMPs are cleaned by tests.')
} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding UTF8
Write-Output "Saved verification summary and $($sources.Count) source hashes."
