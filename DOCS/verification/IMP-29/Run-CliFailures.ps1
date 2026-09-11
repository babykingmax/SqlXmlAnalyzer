#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$BuildEvidence, [ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$context = Start-AcceptanceStage $repository $BuildEvidence
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'CLI failure output must be a new directory.' }
$null = New-Item -ItemType Directory -Path $output
$cli = Join-Path $repository "SqlXmlAnalyzer.CLI/bin/$Configuration/net8.0/SqlXmlAnalyzer.CLI.dll"
$cases = @{
    'malformed'='<ShowPlanXML>';
    'wrong-root'='<unrelated />';
    'dtd'='<!DOCTYPE root [<!ENTITY value "synthetic">]><root>&value;</root>'
}
$runs = foreach ($name in $cases.Keys | Sort-Object) {
    $inputFile = Join-Path $output ($name + '.sqlplan')
    [IO.File]::WriteAllText($inputFile,$cases[$name])
    $hash = (Get-FileHash -LiteralPath $inputFile).Hash
    $stdout = Join-Path $output ($name + '.json')
    & dotnet $cli --path $inputFile --format json 1> $stdout 2> (Join-Path $output ($name + '.log'))
    $code = $LASTEXITCODE
    $result = @(Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json)
    $pass = $code -eq 1 -and $result.Count -eq 1 -and $result[0].Status -eq 'Failed' -and (Get-FileHash -LiteralPath $inputFile).Hash -eq $hash
    [ordered]@{ Case=$name; ExitCode=$code; Status=$result[0].Status; InputStatus=$result[0].InputStatus; Passed=$pass; SourceSha256=$hash }
}
$runs | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding utf8
if (@($runs | Where-Object { -not $_.Passed }).Count) { throw 'CLI invalid-input acceptance failed.' }
Complete-AcceptanceStage $context $output @(Get-ChildItem -LiteralPath $output -File | ForEach-Object Name)
Write-Output "PASS: 3 invalid CLI inputs fail without changing source ($Configuration)"
