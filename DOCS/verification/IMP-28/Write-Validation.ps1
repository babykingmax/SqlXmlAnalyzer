param([switch]$VerifySources)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sourceManifest = Join-Path $PSScriptRoot 'sources.json'
function EvidenceHash([string]$relative) {
    $path = Join-Path $repository $relative
    [ordered]@{ Path = $relative; Bytes = (Get-Item -LiteralPath $path).Length; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
function VerifyHash($record) {
    $path = Join-Path $repository $record.Path
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $record.Sha256) {
        throw "Changed or missing evidence: $($record.Path)"
    }
}
if ($VerifySources) {
    $sources = Get-Content -LiteralPath $sourceManifest -Raw | ConvertFrom-Json
    foreach ($source in $sources) { VerifyHash $source }
    Write-Output "Verified $($sources.Count) source, fixture and documentation hashes."
    return
}
function ReadResults([string]$relative) {
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
            if ($reader.LocalName -eq 'UnitTestResult') {
                $name = $reader.GetAttribute('testName')
                if ($name.Contains('.Compatibility.') -or $name.Contains('.CompatibilityDiagnosticTests.')) {
                    $tests.Add([ordered]@{ Name = $name; Outcome = $reader.GetAttribute('outcome') })
                }
            }
        }
    }
    finally { $reader.Dispose() }
    if ($null -eq $counters) { throw "Missing TRX counters: $relative" }
    [ordered]@{ Counters = $counters; CompatibilityTests = $tests.ToArray(); Trx = (EvidenceHash $relative) }
}

$modes = foreach ($configuration in @('Debug', 'Release')) {
    $buildLog = ".tmp.imp28-build-$($configuration.ToLowerInvariant()).log"
    $buildText = Get-Content -LiteralPath (Join-Path $repository $buildLog) -Raw
    if ($buildText -notmatch '0 个警告' -or $buildText -notmatch '0 个错误' -or $buildText -notmatch '已成功生成') { throw "Build not clean: $configuration" }
    $results = ReadResults ".tmp.imp28-results/$configuration/full.trx"
    if ($results.Counters.Total -ne 2739 -or $results.Counters.Passed -ne 2739 -or $results.Counters.Failed -ne 0 -or $results.Counters.NotExecuted -ne 0 -or
        $results.CompatibilityTests.Count -ne 48 -or @($results.CompatibilityTests | Where-Object Outcome -ne 'Passed').Count -ne 0) {
        throw "Unexpected final tests: $configuration"
    }
    $probePath = "DOCS/verification/IMP-28/latest-$configuration.json"
    $probe = Get-Content -LiteralPath (Join-Path $repository $probePath) -Raw | ConvertFrom-Json
    if (-not $probe.Passed -or $probe.Checks.Count -ne 22 -or @($probe.Commands | Where-Object { $_.ExitCode -ne $_.ExpectedExitCode }).Count -ne 0) { throw "Probe failed: $configuration" }
    foreach ($artifact in $probe.Artifacts) {
        VerifyHash @{ Path = "DOCS/verification/IMP-28/$($probe.Run)/$($artifact.Path)"; Sha256 = $artifact.Sha256 }
    }
    $app = EvidenceHash "bin/$configuration/net8.0-windows/SqlXmlAnalyzer.dll"
    $core = EvidenceHash "bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Core.dll"
    $cli = EvidenceHash "SqlXmlAnalyzer.CLI/bin/$configuration/net8.0/SqlXmlAnalyzer.CLI.dll"
    if ($app.Sha256 -ne $probe.AppSha256 -or $core.Sha256 -ne $probe.CoreSha256 -or $cli.Sha256 -ne $probe.CliSha256) { throw "Probe binaries changed: $configuration" }
    foreach ($name in @('SqlXmlAnalyzer.dll', 'SqlXmlAnalyzer.Core.dll', 'SqlXmlAnalyzer.CLI.dll')) {
        $expected = switch ($name) { 'SqlXmlAnalyzer.dll' { $app }; 'SqlXmlAnalyzer.Core.dll' { $core }; default { $cli } }
        VerifyHash @{ Path = "SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows/$name"; Sha256 = $expected.Sha256 }
    }
    [ordered]@{
        Configuration = $configuration; BuildWarnings = 0; BuildErrors = 0; BuildLog = (EvidenceHash $buildLog)
        Tests = $results.Counters; CompatibilityTests = $results.CompatibilityTests; Trx = $results.Trx
        Probe = (EvidenceHash $probePath); Checks = $probe.Checks; Run = $probe.Run
        Assemblies = @($app, $core, $cli, (EvidenceHash "SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Tests.dll"))
    }
}
$redRuns = foreach ($item in @(@{ Name = 'config'; Failed = 5 }, @{ Name = 'session'; Failed = 10 }, @{ Name = 'session-shape'; Failed = 2 })) {
    $result = ReadResults ".tmp.imp28-results/Debug/$($item.Name)-red.trx"
    if ($result.Counters.Failed -ne $item.Failed) { throw "Missing failure reproduction: $($item.Name)" }
    [ordered]@{ Scenario = $item.Name; Tests = $result.Counters; Trx = $result.Trx }
}
Push-Location $repository
try {
    $fixturePaths = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard -- 'SqlXmlAnalyzer.Tests/TestData/Compatibility/*' | Sort-Object -Unique)
    $fixtures = foreach ($relative in $fixturePaths) { EvidenceHash $relative }
    $fixtures | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'fixture-hashes.json') -Encoding utf8
    $sourcePaths = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.cs' '*.xaml' '*.csproj' '*.props' '*.sln' 'RuleConfiguration.json' |
        Where-Object { $_ -notmatch '(^|/)(\.git|bin|obj|\.vs|publish-[^/]*|backups|\.tmp\.[^/]*)(/|$)' -and $_ -notmatch '^DOCS/' } | Sort-Object -Unique)
    $sourcePaths += $fixturePaths
    $sourcePaths += @('.gitattributes', '.gitignore', 'DOCS/verification/IMP-28/Write-Validation.ps1', 'DOCS/verification/IMP-28/Run-CompatibilityProbe.ps1',
        'DOCS/verification/IMP-28/README.md', 'DOCS/verification/IMP-28/fixtures.md', 'DOCS/verification/IMP-28/fixture-hashes.json',
        'DOCS/verification/IMP-28/latest-Debug.json', 'DOCS/verification/IMP-28/latest-Release.json',
        'DOCS/IMP-28模型配置报告与CLI兼容性.md', 'README.md', 'CHANGELOG.md', 'Architecture.md', 'UserGuide.md', 'DOCS/README.md', 'DOCS/软件改善实施规划.md')
    $sources = foreach ($relative in ($sourcePaths | Sort-Object -Unique)) { EvidenceHash $relative }
    $head = git rev-parse HEAD
}
finally { Pop-Location }
$sources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $sourceManifest -Encoding utf8
[ordered]@{
    Implementation = 'IMP-28'; GeneratedAtUtc = [DateTime]::UtcNow.ToString('O'); GitHead = $head
    Context = 'Local working tree with uncommitted IMP-23 through IMP-28 changes; no commit, package or publication performed.'
    BaselineTests = 2691; AddedTests = 48; SourceManifest = 'sources.json'; SourceCount = $sources.Count
    FixtureManifest = 'fixture-hashes.json'; FixtureCount = $fixtures.Count; Matrix = '../../IMP-28模型配置报告与CLI兼容性.md'
    RedRuns = @($redRuns); Configurations = @($modes); CoverageGenerated = $false
    Limitations = @('Synthetic offline compatibility examples only; no new SQL Server, WPF physical interaction or publication acceptance run.',
        'Version 2.0 session is reconstructed from historical writer fields, not emitted by an old binary; version 2.1 and CLI baselines were emitted by pre-IMP28 Release binaries.',
        'Cancellation exit 130 is tested through the CLI entry token; no new actual Ctrl+C process test.',
        'Full tests include real minidump validation and Debug/Release log gating; temporary test dumps are cleaned up.',
        'TRX and build logs remain local under .tmp.imp28-*; hashes identify bytes, not authenticity. IMP-29 and IMP-30 remain separate work.')
} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8
Write-Output "Saved IMP-28 results, $($fixtures.Count) frozen fixtures and $($sources.Count) source/documentation hashes."
