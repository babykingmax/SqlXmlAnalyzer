param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$assembly = Join-Path $repository "SqlXmlAnalyzer.Core/bin/$Configuration/net8.0/SqlXmlAnalyzer.Core.dll"
[void][System.Reflection.Assembly]::LoadFrom($assembly)
[SqlXmlAnalyzer.Logger]::Initialize($false, $null, $null, $false)
$measurements = @()

# Measure recognition/position construction; XML tree creation is outside the timer.
foreach ($count in 20000, 40000, 80000) {
    $document = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe('<deadlock-list>' + ('<unknown/>' * $count) + '</deadlock-list>')
    $samples = @()
    foreach ($iteration in 1..3) {
        $timer = [System.Diagnostics.Stopwatch]::StartNew()
        $result = [SqlXmlAnalyzer.Core.Services.InputRecognitionService]::Recognize($document, [System.Threading.CancellationToken]::None)
        $timer.Stop()
        if ($result.Diagnostics.Count -ne $count -or $result.Diagnostics[$count - 1].Location.XmlPath -ne "/deadlock-list[1]/unknown[$count]") {
            throw 'Source position verification failed.'
        }
        $samples += $timer.Elapsed.TotalMilliseconds
    }
    $measurements += [pscustomobject]@{
        Records = $count
        MedianMilliseconds = [Math]::Round(($samples | Sort-Object)[1], 3)
        SamplesMilliseconds = @($samples | ForEach-Object { [Math]::Round($_, 3) })
    }
}

$document = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe('<deadlock-list>' + ('<unknown/>' * 80000) + '</deadlock-list>')
$cancellation = [System.Threading.CancellationTokenSource]::new()
$timer = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $cancellation.CancelAfter(1)
    $status = 'Completed'
    try { $null = [SqlXmlAnalyzer.Core.Services.InputRecognitionService]::Recognize($document, $cancellation.Token) }
    catch {
        if ($_.Exception.GetBaseException() -isnot [System.OperationCanceledException]) { throw }
        $status = 'Cancelled'
    }
    $timer.Stop()
    if ($status -ne 'Cancelled') { throw 'Cancellation was not observed.' }
    $report = [pscustomobject]@{
        Configuration = $Configuration
        MeasuredAt = [DateTimeOffset]::Now.ToString('O')
        Scope = 'Recognition and source locations; in-memory XML with repeated unsupported sibling records; not a production throughput benchmark.'
        Measurements = $measurements
        Cancellation = @{ RequestedAfterMilliseconds = 1; ObservedMilliseconds = [Math]::Round($timer.Elapsed.TotalMilliseconds, 3); Status = $status }
    }
    $json = $report | ConvertTo-Json -Depth 6
    if ($OutputPath) { Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8 }
    $json
}
finally { $cancellation.Dispose() }
