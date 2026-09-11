param(
    [string]$Before = (Join-Path $PSScriptRoot 'rule-baseline-complete.json'),
    [string]$After = (Join-Path $PSScriptRoot 'rule-current-complete.json'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'rule-output-differences.json')
)
$ErrorActionPreference = 'Stop'
$baseline = Get-Content -LiteralPath $Before -Raw | ConvertFrom-Json
$current = Get-Content -LiteralPath $After -Raw | ConvertFrom-Json
$rows = foreach ($fixture in $baseline.Fixtures) {
    $match = @($current.Fixtures | Where-Object Path -EQ $fixture.Path)
    if ($match.Count -ne 1 -or $match[0].SourceHash -ne $fixture.SourceHash) { throw "Fixture mismatch: $($fixture.Path)" }
    $oldFindings = ConvertTo-Json -InputObject @($fixture.Findings) -Depth 6 -Compress
    $newFindings = ConvertTo-Json -InputObject @($match[0].Findings) -Depth 6 -Compress
    [ordered]@{Path=$fixture.Path; SourceHash=$fixture.SourceHash; BeforeCount=@($fixture.Findings).Count;
        AfterCount=@($match[0].Findings).Count; LegacyFieldsUnchanged=($oldFindings -ceq $newFindings)}
}
if ($baseline.Fixtures.Count -ne $current.Fixtures.Count) { throw 'Fixture count mismatch' }
[ordered]@{
    MeasuredAt=[DateTimeOffset]::Now.ToString('O'); BeforeAssemblyHash=$baseline.AssemblyHash; AfterAssemblyHash=$current.AssemblyHash
    ComparedFields=@('RuleId','Severity','Title','Message','NodeId'); Fixtures=@($rows)
    Scope='Eight existing fixtures with the same repository rule configuration and ScriptDom dependency loaded; new location/object metadata is additive.'
    IntentionalChanges=@('Text/JSON outputs retain source scope.', 'Table-only and case-insensitive missing-index associations no longer guess.',
        'Multi-query comparison requires explicit selection.', 'All statement SQL and query-plan visualizations are retained; multi-statement automatic rewrite is withheld.',
        'Nested statement and extension ownership follow source hierarchy.')
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
if (@($rows | Where-Object { -not $_.LegacyFieldsUnchanged }).Count) { throw 'Unexpected legacy rule output difference' }
"All $($rows.Count) fixture comparisons passed."
