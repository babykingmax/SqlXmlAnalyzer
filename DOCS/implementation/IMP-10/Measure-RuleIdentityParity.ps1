param([ValidateSet('Debug','Release')][string]$Configuration, [Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
$repository=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$assembly=Join-Path $repository "SqlXmlAnalyzer.Core/bin/$Configuration/net8.0/SqlXmlAnalyzer.Core.dll"
[void][System.Reflection.Assembly]::LoadFrom($assembly)
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $repository "SqlXmlAnalyzer.CLI/bin/$Configuration/net8.0/Microsoft.SqlServer.TransactSql.ScriptDom.dll"))
[SqlXmlAnalyzer.Logger]::Initialize($false,$null,$null,$false)
$paths=@(git -C $repository ls-files 'SqlXmlAnalyzer.Tests/TestData/*.sqlplan' 'SqlXmlAnalyzer.Tests/Resources/*.sqlplan')
$paths+=@('SqlXmlAnalyzer.Tests/TestData/plan_no_relop.sqlplan','SqlXmlAnalyzer.Tests/TestData/plan_prefixed_no_relop.sqlplan','SqlXmlAnalyzer.Tests/TestData/imp07_field_mapping.sqlplan')
$rows=@()
foreach($path in ($paths | Sort-Object -Unique)) {
    $full=Join-Path $repository $path
    $document=[SqlXmlAnalyzer.SafeXmlHelper]::LoadSafe([string]$full)
    $method=[SqlXmlAnalyzer.PlanDiagnosticAnalyzer].GetMethods() | Where-Object {$_.Name -eq 'AnalyzePlan'} | Select-Object -First 1
    $arguments=[object[]]::new($method.GetParameters().Length)
    $arguments[0]=$document
    $arguments[1]=$document.Root.Name.Namespace
    $arguments[2]=[string](Join-Path $repository 'RuleConfiguration.json')
    $results=$method.Invoke($null,$arguments)
    $rows+=@{Path=$path;SourceHash=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash;Findings=@($results | Select-Object RuleId,Severity,Title,Message,NodeId)}
}
@{Configuration=$Configuration;MeasuredAt=[DateTimeOffset]::Now.ToString('O');AssemblyHash=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;Fixtures=$rows} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
"Measured $($rows.Count) fixtures."
