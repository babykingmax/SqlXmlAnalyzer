param([switch]$VerifySources)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sourceManifest = Join-Path $PSScriptRoot 'sources.json'

function Get-EvidenceHash([string]$relative) {
    $path = Join-Path $repository $relative
    [ordered]@{ Path = $relative; Bytes = (Get-Item -LiteralPath $path).Length; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}

if ($VerifySources) {
    $sources = Get-Content -LiteralPath $sourceManifest -Raw | ConvertFrom-Json
    foreach ($source in $sources) {
        $path = Join-Path $repository $source.Path
        if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $source.Sha256) {
            throw "Source changed after validation: $($source.Path)"
        }
    }
    Write-Output "Verified $($sources.Count) source and documentation hashes."
    return
}

function Read-TestResults([string]$relative) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create((Join-Path $repository $relative), $settings)
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
    if ($null -eq $counters) { throw "Missing TRX counters: $relative" }
    [ordered]@{ Counters = $counters; PackageTests = $tests.ToArray(); Trx = (Get-EvidenceHash $relative) }
}

$modes = foreach ($configuration in @('Debug', 'Release')) {
    $lower = $configuration.ToLowerInvariant()
    $buildLog = ".tmp.imp27-hardening-build-$lower.log"
    $buildText = Get-Content -LiteralPath (Join-Path $repository $buildLog) -Raw
    if ($buildText -notmatch '0 个警告' -or $buildText -notmatch '0 个错误' -or $buildText -notmatch '已成功生成') {
        throw "Build not clean: $configuration"
    }
    $results = Read-TestResults ".tmp.imp27-hardening-results/$configuration/full.trx"
    if ($results.Counters.Total -ne 2691 -or $results.Counters.Passed -ne 2691 -or $results.Counters.Failed -ne 0 -or $results.Counters.NotExecuted -ne 0 -or
        $results.PackageTests.Count -ne 46 -or @($results.PackageTests | Where-Object Outcome -ne 'Passed').Count -ne 0) {
        throw "Unexpected final test results: $configuration"
    }
    [ordered]@{
        Configuration = $configuration; BuildWarnings = 0; BuildErrors = 0; BuildLog = (Get-EvidenceHash $buildLog)
        Tests = $results.Counters; PackageTests = $results.PackageTests; Trx = $results.Trx
        Assemblies = @(
            (Get-EvidenceHash "SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Tests.dll")
            (Get-EvidenceHash "SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Core.dll")
        )
    }
}

$redRuns = foreach ($item in @(
    @{ Name = 'rule-versions'; Failed = 1 }, @{ Name = 'metric-gaps'; Failed = 2 }, @{ Name = 'utf8'; Failed = 1 }
)) {
    $results = Read-TestResults ".tmp.imp27-hardening-results/Debug/$($item.Name)-red.trx"
    if ($results.Counters.Failed -ne $item.Failed -or $results.Counters.Passed -ne 0) { throw "Missing failure reproduction: $($item.Name)" }
    [ordered]@{ Scenario = $item.Name; Tests = $results.Counters; Trx = $results.Trx }
}

Push-Location $repository
try {
    $sourcePaths = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.cs' '*.xaml' '*.csproj' '*.props' '*.sln' 'RuleConfiguration.json' |
        Where-Object { $_ -notmatch '(^|/)(\.git|bin|obj|\.vs|publish-[^/]*|backups|\.tmp\.[^/]*)(/|$)' -and $_ -notmatch '^DOCS/' } | Sort-Object -Unique)
    $sourcePaths += @('DOCS/verification/IMP-27-hardening/Write-Validation.ps1', 'DOCS/verification/IMP-27-hardening/README.md',
        'DOCS/IMP-27审查修复与加固说明.md', 'DOCS/IMP-27可审核的本地诊断数据包.md', 'README.md', 'CHANGELOG.md', 'Architecture.md',
        'UserGuide.md', 'DOCS/README.md', 'DOCS/软件改善实施规划.md', 'DOCS/verification/IMP-27/README.md')
    $sources = foreach ($relative in $sourcePaths) { Get-EvidenceHash $relative }
    $head = git rev-parse HEAD
}
finally { Pop-Location }

$sources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $sourceManifest -Encoding UTF8
[ordered]@{
    Implementation = 'IMP-27 review fixes and hardening'; GeneratedAtUtc = [DateTime]::UtcNow.ToString('O'); GitHead = $head
    Context = 'Existing working tree includes uncommitted IMP-23 through IMP-27 work. No commit or publication performed.'
    BaselineTests = 2684; AddedRegressionTests = 7; SourceManifest = 'sources.json'; SourceCount = $sources.Count
    RedRuns = @($redRuns); Configurations = @($modes); CoverageGenerated = $false
    Limitations = @('No UI changes and no new WPF or physical interaction acceptance run; earlier WPF evidence remains historical.',
        'Synthetic offline plans only; no SQL Server connection or production data collection.',
        'Full tests include real minidump generation/validation and Debug/Release log gating; test dumps are cleaned up.',
        'TRX/build logs remain local under .tmp.imp27-hardening-*; hashes establish byte identity, not authenticity.')
} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding UTF8
Write-Output "Saved hardening results and $($sources.Count) source/documentation hashes."
