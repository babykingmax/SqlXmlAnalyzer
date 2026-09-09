param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../..'))
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
# Run in a fresh PowerShell process after the Release build so assembly versions cannot be stale.
[void][Reflection.Assembly]::LoadFrom((Join-Path $repository 'SqlXmlAnalyzer.Core/bin/Release/net8.0/SqlXmlAnalyzer.Core.dll'))
[SqlXmlAnalyzer.Logger]::Initialize($false, [SqlXmlAnalyzer.LogLevel]::Error, $null, $false)
$results = @()
try {
    foreach ($count in @(10000, 50000, 100000)) {
        $xml = [Text.StringBuilder]::new('<deadlock><victim-list>')
        for ($i = 0; $i -lt $count; $i++) { [void]$xml.Append('<victimProcess id="p').Append($i).Append('"/>') }
        [void]$xml.Append('</victim-list><process-list>')
        for ($i = 0; $i -lt $count; $i++) { [void]$xml.Append('<process id="p').Append($i).Append('" spid="51"/>') }
        [void]$xml.Append('</process-list><resource-list><keylock id="single"><owner-list><owner id="p0" mode="X"/></owner-list><waiter-list><waiter id="p1" mode="X"/></waiter-list></keylock></resource-list></deadlock>')
        $document = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe($xml.ToString())
        $elapsed = [Diagnostics.Stopwatch]::StartNew()
        $parsed = [SqlXmlAnalyzer.DeadlockXmlParser]::TryParseDeadlockXml($document, [Threading.CancellationToken]::None, $null)
        $elapsed.Stop()
        if (!$parsed.IsSuccess -or $parsed.Value.VictimIds.Count -ne $count) { throw "Parse mismatch for $count victims" }
        $results += [ordered]@{Processes=$count;Victims=$count;Characters=$xml.Length;ParseMilliseconds=$elapsed.Elapsed.TotalMilliseconds;Warnings=$parsed.Warnings.Count}
    }
    [ordered]@{TimestampUtc=[DateTime]::UtcNow.ToString('O');Configuration='Release';
        Scope='Local one-shot parser measurements; excludes SafeXmlHelper reading; not a CI timing threshold or SQL Server workload benchmark';
        CoreAssemblySha256=(Get-FileHash (Join-Path $repository 'SqlXmlAnalyzer.Core/bin/Release/net8.0/SqlXmlAnalyzer.Core.dll')).Hash;
        Results=$results} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'performance.json') -Encoding utf8
    $results | ConvertTo-Json
}
finally { [SqlXmlAnalyzer.Logger]::Shutdown() }
