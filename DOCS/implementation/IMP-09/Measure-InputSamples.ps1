param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path)
$ErrorActionPreference = 'Stop'
$samples = @(
    Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'SqlXmlAnalyzer.Tests/TestData') -File |
        Where-Object Extension -in '.sqlplan', '.xdl'
    Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'SqlXmlAnalyzer.Tests/Resources') -File |
        Where-Object Extension -in '.sqlplan', '.xdl'
)
$results = foreach ($sample in $samples) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($sample.FullName, $settings)
    $nodes = 0L
    $depth = 0
    try {
        while ($reader.Read()) {
            $nodes += 1L + $reader.AttributeCount
            $depth = [Math]::Max($depth, $reader.Depth + 1)
        }
        [pscustomobject]@{
            File = [IO.Path]::GetRelativePath($RepositoryRoot, $sample.FullName)
            Bytes = $sample.Length
            Characters = [IO.File]::ReadAllText($sample.FullName).Length
            NodesIncludingAttributes = $nodes
            Depth = $depth
        }
    }
    finally { $reader.Dispose() }
}
$results | Sort-Object File | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $PSScriptRoot 'input-samples.json') -Encoding utf8
