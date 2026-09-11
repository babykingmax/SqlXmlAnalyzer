param([ValidateSet('before','after')][string]$Phase = 'after', [int[]]$Sizes = @(100,1000,5000))
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path $PSScriptRoot $Phase
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($Phase -eq 'before') {
    & (Join-Path $PSScriptRoot 'Build-BaselineProbe.ps1')
    $hostDirectory = (Get-Content -LiteralPath (Join-Path $output 'host-path.txt') -Raw).Trim()
}
else {
$hostDirectory = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP26-Perf-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $hostDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Destination $hostDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PerformanceProbe.cs.txt') -Destination (Join-Path $hostDirectory 'Program.cs')
$app = [Security.SecurityElement]::Escape((Join-Path $repository 'SqlXmlAnalyzer.csproj'))
$analysis = [Security.SecurityElement]::Escape((Join-Path $repository 'src/SqlXmlAnalyzer.Analysis/SqlXmlAnalyzer.Analysis.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="$app"/><ProjectReference Include="$analysis"/></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $hostDirectory 'Probe.csproj') -Encoding UTF8
dotnet build (Join-Path $hostDirectory 'Probe.csproj') -c Release -m:1 -p:IntermediateOutputPath=obj/imp26/Release/ -v minimal > (Join-Path $output 'probe-build.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Performance probe build failed' }
Set-Content -LiteralPath (Join-Path $output 'host-path.txt') -Value $hostDirectory
}
$cpu = Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors
$computer = Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory
@{ Cpu=$cpu; Computer=$computer; OS=[Environment]::OSVersion.ToString(); Dotnet=(dotnet --version); CapturedAtUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'hardware.json')
$fixtureDirectory = Join-Path $PSScriptRoot 'fixtures'
New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
foreach ($size in $Sizes) {
    $ns = 'http://schemas.microsoft.com/sqlserver/2004/07/showplan'
    $xml = [xml]"<ShowPlanXML xmlns='$ns' Version='1.539' Build='16.0.1000.6'><BatchSequence><Batch><Statements><StmtSimple StatementId='1' StatementText='SELECT 1' StatementType='SELECT'><QueryPlan/></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>"
    $nodes = @()
    for ($i=0; $i -lt $size; $i++) {
        $node = $xml.CreateElement('RelOp', $ns)
        $node.SetAttribute('NodeId', "$i"); $node.SetAttribute('PhysicalOp','Concatenation'); $node.SetAttribute('LogicalOp','Concatenation')
        $node.SetAttribute('EstimateRows','10'); $node.SetAttribute('EstimatedTotalSubtreeCost','1'); $node.SetAttribute('EstimatedCPUCost','0.01'); $node.SetAttribute('EstimatedIOCost','0.01')
        [void]$node.AppendChild($xml.CreateElement('Concat', $ns)); $nodes += $node
        if ($i -gt 0) { [void]$nodes[[int][Math]::Floor(($i-1)/2)].FirstChild.AppendChild($node) }
    }
    [void]$xml.DocumentElement.FirstChild.FirstChild.FirstChild.FirstChild.FirstChild.AppendChild($nodes[0])
    $inputFile = Join-Path $fixtureDirectory "plan-$size.sqlplan"
    $xml.Save($inputFile)
    $resultFile = Join-Path $output "plan-$size.json"
    $process = Start-Process -FilePath (Join-Path $hostDirectory 'bin/Release/net8.0-windows/Probe.exe') -ArgumentList @('"'+$inputFile+'"','"'+$resultFile+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output "plan-$size.stdout.log") -RedirectStandardError (Join-Path $output "plan-$size.stderr.log")
    if (-not $process.WaitForExit(180000)) { Stop-Process -Id $process.Id; @{ Passed=$false; Error='Process exceeded 180 second measurement budget'; SourceSha256=(Get-FileHash $inputFile).Hash } | ConvertTo-Json | Set-Content -LiteralPath $resultFile }
    Write-Output "$Phase measurement: $size operators"
}
