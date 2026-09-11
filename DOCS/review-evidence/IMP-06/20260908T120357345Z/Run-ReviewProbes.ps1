param(
    [string]$ArtifactsRoot = 'E:\SqlXmlAnalyzer\bin\imp06-verification-d9988486ac9f45e48dae67fb2fb2a747\bin'
)
$ErrorActionPreference = 'Stop'
$cli = Join-Path $ArtifactsRoot 'SqlXmlAnalyzer.CLI\release\SqlXmlAnalyzer.CLI.dll'
$ns = 'http://schemas.microsoft.com/sqlserver/2004/07/showplan'
$outputRoot = $PSScriptRoot
$utf8 = [Text.UTF8Encoding]::new($false)
$valid = [IO.File]::ReadAllText('E:\SqlXmlAnalyzer\SqlXmlAnalyzer.Tests\TestData\plan_no_relop.sqlplan')
$wrongNamespace = @"
<?xml version="1.0" encoding="utf-8"?>
<ShowPlanXML xmlns="$ns" Version="1.539" Build="15.0.2000.5"><BatchSequence><Batch><Statements>
<StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT * FROM ReviewOnlyTable;">
<QueryPlan><RelOp xmlns="urn:wrong" NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimatedTotalSubtreeCost="12" /></QueryPlan>
</StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>
"@
[IO.File]::WriteAllText((Join-Path $outputRoot 'wrong-relop-namespace.sqlplan'), $wrongNamespace, $utf8)
[IO.File]::WriteAllText((Join-Path $outputRoot 'correct-relop-namespace.sqlplan'), $wrongNamespace.Replace(' xmlns="urn:wrong"', ''), $utf8)
[IO.File]::WriteAllText((Join-Path $outputRoot 'valid-utf8.sqlplan'), $valid, $utf8)
$utf16 = [Text.UnicodeEncoding]::new($false, $false, $true)
[IO.File]::WriteAllText((Join-Path $outputRoot 'valid-utf16-no-bom.sqlplan'), $valid.Replace('encoding="utf-8"','encoding="utf-16"'), $utf16)
$utf32 = [Text.UTF32Encoding]::new($false, $false, $true)
[IO.File]::WriteAllText((Join-Path $outputRoot 'valid-utf32-no-bom.sqlplan'), $valid.Replace('encoding="utf-8"','encoding="utf-32"'), $utf32)
$badByteXml = $valid.Replace('CREATE TABLE #imp06 (Id int);', "SELECT N'REPLACE_BYTE';")
$badBytes = $utf8.GetBytes($badByteXml)
$markerBytes = $utf8.GetBytes('REPLACE_BYTE')
$markerIndex = $badByteXml.IndexOf('REPLACE_BYTE')
# The fixture is ASCII, so character and UTF-8 byte offsets are equal.
$badBytes[$markerIndex] = 255
[IO.File]::WriteAllBytes((Join-Path $outputRoot 'invalid-utf8-byte.sqlplan'), $badBytes)
[IO.File]::WriteAllText((Join-Path $outputRoot 'input.sql'), 'SELECT 1;', $utf8)
function Invoke-Cli([string]$Name, [string[]]$CliArguments) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $utf8
    $start.StandardErrorEncoding = $utf8
    $start.ArgumentList.Add($cli)
    foreach ($argument in $CliArguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw "Probe timed out: $Name" }
    [IO.File]::WriteAllText((Join-Path $outputRoot "$Name.stdout.txt"), $stdout.GetAwaiter().GetResult(), $utf8)
    [IO.File]::WriteAllText((Join-Path $outputRoot "$Name.stderr.txt"), $stderr.GetAwaiter().GetResult(), $utf8)
    [pscustomobject]@{ Name=$Name; ExitCode=$process.ExitCode; Arguments=$CliArguments }
    $process.Dispose()
}
$results = @()
foreach ($fixture in @('correct-relop-namespace','wrong-relop-namespace','valid-utf8','valid-utf16-no-bom','valid-utf32-no-bom','invalid-utf8-byte')) {
    $results += Invoke-Cli $fixture @('--path',(Join-Path $outputRoot "$fixture.sqlplan"),'--block-scans','--max-cost','1','--format','json','--output',(Join-Path $outputRoot "$fixture.report.json"))
}
foreach ($fixture in @('valid-utf8','valid-utf16-no-bom','valid-utf32-no-bom')) {
    $results += Invoke-Cli "refactor-$fixture" @('refactor',(Join-Path $outputRoot 'input.sql'),'--plan',(Join-Path $outputRoot "$fixture.sqlplan"),'--dry-run','--output',(Join-Path $outputRoot "refactor-$fixture.report.json"))
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputRoot 'cli-probes.json') -Encoding utf8
$results | Select-Object Name,ExitCode | Format-Table -AutoSize

$null = [Reflection.Assembly]::LoadFrom((Join-Path $ArtifactsRoot 'SqlXmlAnalyzer.Core\release\SqlXmlAnalyzer.Core.dll'))
$inputService = [SqlXmlAnalyzer.Core.Services.InputRecognitionService]::new($null,$null,$null)
$openService = [SqlXmlAnalyzer.Core.Services.DocumentOpenService]::new($inputService)
$encodingObserved = foreach ($fixture in @('invalid-utf8-byte','valid-utf16-no-bom','valid-utf32-no-bom')) {
    $path = Join-Path $outputRoot "$fixture.sqlplan"
    $strictError = $null
    try { $null = [SqlXmlAnalyzer.SafeXmlHelper]::LoadSafe($path) }
    catch { $strictError = $_.Exception.InnerException.Message }
    $recognized = $inputService.Load($path,[Threading.CancellationToken]::None)
    $opened = $openService.OpenAsync($path,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    [pscustomobject]@{File="$fixture.sqlplan";StrictXmlError=$strictError;SharedInputStatus=$recognized.Status.ToString();GuiOpenStatus=$opened.Status.ToString();HasReplacementCharacter=$recognized.Document.ToString().Contains([char]65533)}
}
$encodingObserved | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'encoding-observed.json') -Encoding utf8
$schemas = [Xml.Schema.XmlSchemaSet]::new()
$schemas.XmlResolver = $null
$null = $schemas.Add($ns, 'E:\SqlXmlAnalyzer\SqlXmlAnalyzer.Tests\TestData\Schemas\showplanxml-sql2019.xsd')
$document = [SqlXmlAnalyzer.SafeXmlHelper]::LoadSafe((Join-Path $outputRoot 'valid-utf32-no-bom.sqlplan'))
[Xml.Schema.Extensions]::Validate($document, $schemas, $null)
[pscustomobject]@{File='valid-utf32-no-bom.sqlplan';StrictXmlLoad=$true;FullShowPlanXsdValidation=$true;Declaration=$document.Declaration.ToString()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'utf32-validation.json') -Encoding utf8
