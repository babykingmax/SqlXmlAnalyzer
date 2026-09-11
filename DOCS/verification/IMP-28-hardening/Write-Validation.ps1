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
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $record.Sha256) { throw "Changed evidence: $($record.Path)" }
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
    $diagnostics = [Collections.Generic.List[object]]::new()
    $counters = $null
    try {
        while ($reader.Read()) {
            if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
            if ($reader.LocalName -eq 'Counters') {
                $counters = [ordered]@{ Total = [int]$reader.GetAttribute('total'); Passed = [int]$reader.GetAttribute('passed'); Failed = [int]$reader.GetAttribute('failed'); NotExecuted = [int]$reader.GetAttribute('notExecuted') }
            }
            if ($reader.LocalName -eq 'UnitTestResult') {
                $name = $reader.GetAttribute('testName')
                $result = [ordered]@{ Name = $name; Outcome = $reader.GetAttribute('outcome') }
                if ($name.Contains('.Compatibility.') -or $name.Contains('.CompatibilityDiagnosticTests.')) { $tests.Add($result) }
                if ($name.Contains('.CompatibilityDiagnosticTests.') -or $name.Contains('.RuleConfigurationDiagnosticTests.')) { $diagnostics.Add($result) }
            }
        }
    }
    finally { $reader.Dispose() }
    if ($null -eq $counters) { throw "Missing TRX counters: $relative" }
    [ordered]@{ Counters = $counters; CompatibilityTests = $tests.ToArray(); DiagnosticTests = $diagnostics.ToArray(); Trx = (EvidenceHash $relative) }
}
$modes = foreach ($configuration in @('Debug', 'Release')) {
    $buildLog = ".tmp.imp28-hardening-build-$($configuration.ToLowerInvariant()).log"
    $build = Get-Content -LiteralPath (Join-Path $repository $buildLog) -Raw
    if ($build -notmatch '0 个警告' -or $build -notmatch '0 个错误' -or $build -notmatch '已成功生成') { throw "Build not clean: $configuration" }
    $results = ReadResults ".tmp.imp28-hardening-results/$configuration/full.trx"
    $migration = @($results.CompatibilityTests | Where-Object { $_.Name.Contains('.ConfigurationMigrationHardeningTests.') })
    if ($results.Counters.Total -ne 2754 -or $results.Counters.Passed -ne 2754 -or $results.Counters.Failed -ne 0 -or $results.Counters.NotExecuted -ne 0 -or
        $results.CompatibilityTests.Count -ne 63 -or $migration.Count -ne 15 -or $results.DiagnosticTests.Count -lt 3) { throw "Unexpected final tests: $configuration" }
    $core = EvidenceHash "SqlXmlAnalyzer.Core/bin/$configuration/net8.0/SqlXmlAnalyzer.Core.dll"
    foreach ($copy in @("bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Core.dll", "SqlXmlAnalyzer.CLI/bin/$configuration/net8.0/SqlXmlAnalyzer.Core.dll",
        "SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Core.dll")) { VerifyHash @{ Path = $copy; Sha256 = $core.Sha256 } }
    [ordered]@{
        Configuration = $configuration; BuildWarnings = 0; BuildErrors = 0; BuildLog = (EvidenceHash $buildLog)
        Tests = $results.Counters; CompatibilityTestCount = $results.CompatibilityTests.Count; MigrationTests = $migration; DiagnosticTests = $results.DiagnosticTests; Trx = $results.Trx
        Assemblies = @($core, (EvidenceHash "SqlXmlAnalyzer.Tests/bin/$configuration/net8.0-windows/SqlXmlAnalyzer.Tests.dll"))
    }
}
$redRuns = foreach ($item in @(@{ Name = 'indentation'; Failed = 1 }, @{ Name = 'escaping'; Failed = 4 })) {
    $result = ReadResults ".tmp.imp28-hardening-results/Debug/$($item.Name)-red.trx"
    if ($result.Counters.Failed -ne $item.Failed -or $result.Counters.Passed -ne 0) { throw "Missing failure reproduction: $($item.Name)" }
    [ordered]@{ Scenario = $item.Name; Tests = $result.Counters; Trx = $result.Trx }
}
$fixtures = Get-Content -LiteralPath (Join-Path $repository 'DOCS/verification/IMP-28/fixture-hashes.json') -Raw | ConvertFrom-Json
foreach ($fixture in $fixtures) { VerifyHash $fixture }
Push-Location $repository
try {
    $sourcePaths = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.cs' '*.xaml' '*.csproj' '*.props' '*.sln' 'RuleConfiguration.json' |
        Where-Object { $_ -notmatch '(^|/)(\.git|bin|obj|\.vs|publish-[^/]*|backups|\.tmp\.[^/]*)(/|$)' -and $_ -notmatch '^DOCS/' } | Sort-Object -Unique)
    $sourcePaths += $fixtures.Path
    $sourcePaths += @('.gitattributes', '.gitignore', 'DOCS/verification/IMP-28-hardening/Write-Validation.ps1', 'DOCS/verification/IMP-28-hardening/README.md',
        'DOCS/verification/IMP-28/fixture-hashes.json', 'DOCS/verification/IMP-28/README.md', 'DOCS/IMP-28审查修复与加固说明.md', 'DOCS/IMP-28模型配置报告与CLI兼容性.md',
        'README.md', 'CHANGELOG.md', 'Architecture.md', 'UserGuide.md', 'DOCS/README.md', 'DOCS/软件改善实施规划.md')
    $sources = foreach ($relative in ($sourcePaths | Sort-Object -Unique)) { EvidenceHash $relative }
    $head = git rev-parse HEAD
}
finally { Pop-Location }
$sources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $sourceManifest -Encoding utf8
[ordered]@{
    Implementation = 'IMP-28 review fix and hardening'; GeneratedAtUtc = [DateTime]::UtcNow.ToString('O'); GitHead = $head
    Context = 'Local working tree with uncommitted IMP-23 through IMP-28 changes; no commit, package or publication performed.'
    BaselineTests = 2739; AddedRegressionTests = 15; SourceManifest = 'sources.json'; SourceCount = $sources.Count; UnchangedFrozenFixtureCount = $fixtures.Count
    RedRuns = @($redRuns); Configurations = @($modes); CoverageGenerated = $false
    Limitations = @('Synthetic offline configuration and file-save regressions; no new SQL Server, WPF physical interaction, independent CLI process or publication acceptance run.',
        'Full tests include real minidump validation and Debug/Release log gating; temporary test dumps are cleaned up.',
        'TRX and build logs remain local under .tmp.imp28-hardening-*; hashes identify bytes, not authenticity. IMP-29 and IMP-30 remain separate work.')
} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8
Write-Output "Saved IMP-28 hardening results and $($sources.Count) source/fixture/documentation hashes."
