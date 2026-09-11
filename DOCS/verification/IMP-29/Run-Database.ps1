#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$BuildEvidence, [ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AcceptanceEvidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$context = Start-AcceptanceStage $repository $BuildEvidence
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Database output must be a new directory.' }
$null = New-Item -ItemType Directory -Path $output
$shell = (Get-Process -Id $PID).Path
function Quoted([string]$value) { "'" + $value.Replace("'", "''") + "'" }
$suites = @(
    @{Name='semantics'; Source='IMP-19/Run-Regression.ps1'; Parameter=$true},
    @{Name='apply'; Source='IMP-19/Run-ApplyCheck.ps1'; Parameter=$true},
    @{Name='observer'; Source='IMP-18-19-review-fixes/Run-ObserverRegression.ps1'; Variable='outputDirectory'},
    @{Name='types'; Source='IMP-18-19-review-fixes/Run-ReviewFixes.ps1'; Variable='dir'}
)
$runs = foreach ($suite in $suites) {
    $sourcePath = Join-Path $PSScriptRoot ('../' + $suite.Source)
    $destination = Join-Path $output $suite.Name
    $arguments = @('-NoProfile','-File',$sourcePath,'-Configuration',$Configuration)
    if ($suite.Parameter) { $arguments += @('-OutputDirectory',$destination) }
    else {
        # Only the output and repository assignments change; original SQL assertions remain intact.
        $source = Get-Content -LiteralPath $sourcePath -Raw
        $repoPattern = '(?m)^\$repo = .*\r?$'
        $outputPattern = '(?m)^\$' + $suite.Variable + ' = .*\r?$'
        if ([regex]::Matches($source,$repoPattern).Count -ne 1 -or [regex]::Matches($source,$outputPattern).Count -ne 1) { throw 'SQL probe adapter contract changed.' }
        $source = [regex]::Replace($source, $repoPattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) '$repo = ' + (Quoted $repository) })
        $assignment = '$' + $suite.Variable + ' = ' + (Quoted $destination)
        $source = [regex]::Replace($source, $outputPattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) $assignment })
        $copy = Join-Path $output ($suite.Name + '.ps1')
        [IO.File]::WriteAllText($copy, $source)
        $arguments[2] = $copy
    }
    & $shell @arguments > (Join-Path $output ($suite.Name + '.log')) 2>&1
    $code = $LASTEXITCODE
    [ordered]@{ Suite=$suite.Name; Source=$suite.Source; SourceSha256=(Get-FileHash -LiteralPath $sourcePath).Hash; ExitCode=$code; Passed=($code -eq 0) }
}
$runs | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
if (@($runs | Where-Object { -not $_.Passed }).Count) { throw 'Database acceptance failed; inspect suite logs.' }
$resultFiles = @('runs.json') + @($(foreach ($suite in $suites) { foreach ($file in Get-ChildItem -LiteralPath (Join-Path $output $suite.Name) -File) { $suite.Name + '/' + $file.Name } }))
Complete-AcceptanceStage $context $output $resultFiles
Write-Output "PASS: SQL Server 2019/2025 semantic, observer, type and reviewed-apply suites ($Configuration)"
