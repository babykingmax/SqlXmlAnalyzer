#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryPath = (Join-Path $PSScriptRoot '../../..'), [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new verification directory; historical evidence is never overwritten.' }
[void][IO.Directory]::CreateDirectory($output)
$tracked = @(git -C $repository -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot obtain Git inventory.' }
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($name in @('PrivacyVerification.cs.txt','Run-PrivacyVerification.ps1')) {
    [IO.File]::Copy((Join-Path $PSScriptRoot $name), (Join-Path $output $name), $false)
}
$module = Join-Path $output 'AcceptanceInfrastructure.psm1'
[IO.File]::Copy((Join-Path $repository 'DOCS/acceptance/IMP-02/AcceptanceInfrastructure.psm1'), $module, $false)
Import-Module $module -Force
$before = Get-AcceptanceInputSnapshot $repository
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP05-Process-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporary)
$tests = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; $tests.Add([pscustomobject]@{ name=$Name; passed=$true; error=$null }) }
    catch { $tests.Add([pscustomobject]@{ name=$Name; passed=$false; error=$_.Exception.ToString() }) }
}
function Require([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Run([string]$Stem, [string[]]$Arguments) {
    $r = Invoke-AcceptanceProcess 'dotnet' $Arguments $repository $Stem 120000
    Require (-not $r.timedOut -and $r.outputComplete -and $null -eq $r.failure) "Command did not complete: $Stem"
    return $r
}
try {
    [IO.File]::Copy((Join-Path $output 'PrivacyVerification.cs.txt'), (Join-Path $temporary 'Program.cs'), $false)
    $reference = [Security.SecurityElement]::Escape((Join-Path $repository 'SqlXmlAnalyzer.csproj'))
    $project = "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=`"$reference`" /><PackageReference Include=`"System.Text.Json`" Version=`"10.0.9`" /></ItemGroup></Project>"
    $projectPath = Join-Path $temporary 'PrivacyVerification.csproj'
    [IO.File]::WriteAllText($projectPath,$project,$utf8)
    [IO.File]::WriteAllText((Join-Path $output 'PrivacyVerification.csproj.snapshot'),$project,$utf8)
    foreach ($mode in @('Debug','Release')) {
        $modeRoot=Join-Path $output $mode; [void][IO.Directory]::CreateDirectory($modeRoot)
        $build=Run (Join-Path $modeRoot 'build') @('build',$projectPath,'-c',$mode,'--nologo')
        Require ($build.exitCode -eq 0) "$mode probe build failed"
        $process=Run (Join-Path $modeRoot 'process') @((Join-Path $temporary "bin/$mode/net8.0-windows/PrivacyVerification.dll"),$modeRoot)
        Check "$mode integration process" { Require ($process.exitCode -eq 0) 'Inspect process-results.json for failing assertions.' }
        $cli=Join-Path $repository "SqlXmlAnalyzer.CLI/bin/$mode/net8.0/SqlXmlAnalyzer.CLI.dll"
        $planInput=Join-Path $modeRoot 'SECRET_IMP05_PROCESS_7238.sqlplan'
        $inputHash=(Get-FileHash -LiteralPath $planInput).Hash
        Check "$mode standalone redaction success" {
            $target=Join-Path $modeRoot 'cli-redacted.sqlplan'
            $r=Run (Join-Path $modeRoot 'cli-redact') @($cli,'redact',$planInput,'--output',$target)
            $json=[IO.File]::ReadAllText((Join-Path $modeRoot 'cli-redact.stdout.txt')) | ConvertFrom-Json
            Require ($r.exitCode -eq 0 -and $json.IsSuccess -and $json.Redaction.CanExport) 'CLI redaction failed'
            Require (-not [IO.File]::ReadAllText($target).Contains('SECRET_IMP05') -and -not ($json | ConvertTo-Json -Depth 10).Contains('SECRET_IMP05')) 'CLI leaked marker'
        }
        Check "$mode standalone rejects unknown fields" {
            $target=Join-Path $modeRoot 'cli-blocked.sqlplan'
            $r=Run (Join-Path $modeRoot 'cli-blocked') @($cli,'redact',(Join-Path $modeRoot 'unknown.sqlplan'),'--output',$target)
            $json=[IO.File]::ReadAllText((Join-Path $modeRoot 'cli-blocked.stdout.txt')) | ConvertFrom-Json
            Require ($r.exitCode -eq 1 -and -not $json.IsSuccess -and $null -eq $json.Redaction.Xml -and -not (Test-Path -LiteralPath $target)) 'Unknown input was published'
        }
        Check "$mode standalone protects existing input" {
            $r=Run (Join-Path $modeRoot 'cli-same-path') @($cli,'redact',$planInput,'--output',$planInput)
            Require ($r.exitCode -eq 1 -and (Get-FileHash -LiteralPath $planInput).Hash -ceq $inputHash) 'Input was overwritten'
        }
        foreach ($format in @('json','junit')) {
            Check "$mode raw scan $format privacy label" {
                $r=Run (Join-Path $modeRoot "cli-raw-$format") @($cli,'--path',$planInput,'--format',$format)
                $text=[IO.File]::ReadAllText((Join-Path $modeRoot "cli-raw-$format.stdout.txt"))
                if ($format -eq 'json') { $text=($text | ConvertFrom-Json | ConvertTo-Json -Depth 12) }
                Require ($text.Contains('未脱敏') -and $text.Contains('SECRET_IMP05')) "Missing raw $format notice or fixture marker"
                Require ($r.exitCode -in @(0,1)) 'Scan did not execute'
            }
        }
        Check "$mode raw refactor JSON privacy label and original SQL retained" {
            $sql=Join-Path $modeRoot 'input.sql'; [IO.File]::WriteAllText($sql,"SELECT 'SECRET_IMP05_PROCESS_7238';",$utf8)
            $r=Run (Join-Path $modeRoot 'cli-refactor') @($cli,'refactor',$sql,'--dry-run','--show-sql','--format','json')
            $text=[IO.File]::ReadAllText((Join-Path $modeRoot 'cli-refactor.stdout.txt'))
            $json=$text | ConvertFrom-Json
            Require ($r.exitCode -eq 0 -and $json.Privacy.Contains('未脱敏') -and $text.Contains('SECRET_IMP05')) 'Raw refactor contract failed'
        }
        Check "$mode raw report cannot silently accept redaction flag" {
            $r=Run (Join-Path $modeRoot 'cli-unsupported-redact') @($cli,'--path',$planInput,'--format','json','--redacted')
            Require ($r.exitCode -eq 2 -and [IO.File]::ReadAllText((Join-Path $modeRoot 'cli-unsupported-redact.stdout.txt')).Length -eq 0) 'Unsupported redaction request accepted'
        }
    }
    $drift=@(Compare-AcceptanceInputSnapshot $before (Get-AcceptanceInputSnapshot $repository))
    Check 'Protected source remained stable during all process runs' { Require ($drift.Count -eq 0) 'Source changed during verification' }
} finally {
    $full=[IO.Path]::GetFullPath($temporary)
    $tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($full).StartsWith('SqlXmlAnalyzer-IMP05-Process-')) { throw 'Unexpected cleanup target' }
    Remove-Item -LiteralPath $full -Recurse -Force
    [IO.File]::WriteAllText((Join-Path $output 'verification-results.json'),([pscustomobject]@{
        checkedUtc=[DateTimeOffset]::UtcNow.ToString('o'); head=$before.head; total=$tests.Count;
        passed=@($tests | Where-Object passed -EQ $true).Count; failed=@($tests | Where-Object passed -EQ $false).Count;
        tests=$tests.ToArray(); inputBefore=$before; inputAfter=(Get-AcceptanceInputSnapshot $repository)
    } | ConvertTo-Json -Depth 12),$utf8)
}
if (@($tests | Where-Object passed -EQ $false).Count -gt 0) { exit 1 }
