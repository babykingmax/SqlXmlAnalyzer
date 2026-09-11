param(
    [Parameter(Mandatory)][ValidateSet('Debug','Release')][string]$Configuration,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$assemblyPath = Join-Path $repository "SqlXmlAnalyzer.Core/bin/$Configuration/net8.0/SqlXmlAnalyzer.Core.dll"
[void][Reflection.Assembly]::LoadFrom($assemblyPath)
$previousOut = [Console]::Out
$previousError = [Console]::Error
[Console]::SetOut([IO.TextWriter]::Null)
[Console]::SetError([IO.TextWriter]::Null)
try {
$token = [Threading.CancellationToken]::None
$factory = [SqlXmlAnalyzer.Core.Reporting.DiagnosticReportFactory].GetMethod('Plan')
$renderer = [SqlXmlAnalyzer.Core.Reporting.DiagnosticReportRenderer].GetMethod('Text')
function New-Report($document, $model, $diagnostics, $statement, $query) {
    $arguments = @($document, $model, $diagnostics, $statement, $query, $null, $null)
    if ($factory.GetParameters().Count -eq 8) { $arguments += $token }
    return $factory.Invoke($null, $arguments)
}
function Analyze($document) {
    $model = [SqlXmlAnalyzer.Core.Services.PlanIdentityAdapter]::GetDocument($document, $token)
    $engine = [SqlXmlAnalyzer.Core.Rules.RuleEngine]::new($null, $null)
    $diagnostics = $engine.AnalyzePlanDetailed($document, $document.Root.Name.Namespace, $model, $null, $token)
    return @($model, $diagnostics)
}
$body = [Text.StringBuilder]::new()
0..999 | ForEach-Object { [void]$body.Append('<RelOp NodeId="').Append($_).Append('" />') }
$planXml = '<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan"><BatchSequence><Batch><Statements><StmtSimple><QueryPlan>' + $body.ToString() + '</QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>'
$source = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe($planXml)
$model, $diagnostics = Analyze $source
$start = [GC]::GetAllocatedBytesForCurrentThread()
$report = New-Report $source $model $diagnostics $null $null
$factoryAllocated = [GC]::GetAllocatedBytesForCurrentThread() - $start
$arguments = @($report)
if ($renderer.GetParameters().Count -eq 2) { $arguments += $token }
$start = [GC]::GetAllocatedBytesForCurrentThread()
$text = $renderer.Invoke($null, $arguments)
$textAllocated = [GC]::GetAllocatedBytesForCurrentThread() - $start
$measurements = [ordered]@{
    configuration=$Configuration; assemblySha256=(Get-FileHash -LiteralPath $assemblyPath).Hash
    factoryParameterCount=$factory.GetParameters().Count; runtime=[Environment]::Version.ToString()
    inputCharacters=$planXml.Length; operators=$model.Operators.Count; reportFields=$report.FactCount
    textCharacters=$text.Length; factoryAllocatedBytes=$factoryAllocated; textAllocatedBytes=$textAllocated
}
$schemas = [System.Xml.Schema.XmlSchemaSet]::new()
$schemas.XmlResolver = $null
$schemaDocument = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe([IO.File]::ReadAllText((Join-Path $repository 'SqlXmlAnalyzer.Tests/TestData/Schemas/showplanxml-sql2019.xsd')))
$reader = $schemaDocument.CreateReader()
try { [void]$schemas.Add([System.Xml.Schema.XmlSchema]::Read($reader, $null)) } finally { $reader.Dispose() }
$schemas.Compile()
$errors = [System.Collections.Generic.List[string]]::new()
$handler = [System.Xml.Schema.ValidationEventHandler]{ param($sender,$eventArgs) $errors.Add($eventArgs.Message) }
$cursor = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe([IO.File]::ReadAllText((Join-Path $repository 'SqlXmlAnalyzer.Tests/TestData/imp22_cursor_export.sqlplan')))
[System.Xml.Schema.Extensions]::Validate($cursor, $schemas, $handler)
$measurements.cursorInputXsdErrors = @($errors.ToArray())
$model, $diagnostics = Analyze $cursor
$measurements.cursorOutputs = @(foreach ($index in @(0, 1)) {
    $errors.Clear()
    $selected = New-Report $cursor $model $diagnostics $model.Statements[0].Key $model.QueryPlans[$index].Key
    $document = [SqlXmlAnalyzer.SafeXmlHelper]::ParseSafe($selected.SourceXml)
    [System.Xml.Schema.Extensions]::Validate($document, $schemas, $handler)
    [ordered]@{queryPlan=$index + 1; xsdErrors=@($errors.ToArray())}
})
$json = $measurements | ConvertTo-Json -Depth 8
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
$output = [IO.FileStream]::new([IO.Path]::GetFullPath($OutputPath), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $output.Write($bytes); $output.Flush($true) } finally { $output.Dispose() }
} finally {
    [Console]::SetOut($previousOut)
    [Console]::SetError($previousError)
}
$json
