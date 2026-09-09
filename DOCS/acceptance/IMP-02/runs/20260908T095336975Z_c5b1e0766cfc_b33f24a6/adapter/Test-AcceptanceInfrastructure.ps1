#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'AcceptanceInfrastructure.psm1') -Force
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PlanRowBindingInspector.cs.txt')))
$results = [Collections.Generic.List[object]]::new()
$utf8 = [Text.UTF8Encoding]::new($false)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot ('regressions/' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N') + '.json')
}
if (Test-Path -LiteralPath $OutputPath) { throw 'Regression evidence must use a new path.' }

function Check([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $results.Add([pscustomobject]@{ name=$Name; status='Passed'; error=$null })
    } catch {
        $results.Add([pscustomobject]@{ name=$Name; status='Failed'; error=$_.Exception.Message })
    }
}
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Body) {
    $threw = $false
    try { & $Body } catch { $threw = $true }
    Assert-True $threw 'Expected the operation to fail.'
}
function New-RefactorObservation([string]$Sql) {
    $rule = if ($Sql.Contains('LTRIM')) { 'REF_RULE_103_TRIM' } else { 'REF_RULE_002_TABLE_VAR' }
    [pscustomobject]@{ IsSuccess=$true; OutputSql=$Sql; Errors=@(); parseErrorCount=0; ruleFailures=@(); changeCount=0;
        warnings=@("$rule synthetic skip"); safetySkips=@([pscustomobject]@{RuleId=$rule; ReasonCode='Synthetic'; Reason='Blocked'; RequiredEvidence='Semantic verification'}) }
}
function New-Column([string]$Header, [string]$Binding, [switch]$PropertyElement) {
    $column = [Xml.Linq.XElement]::new([Xml.Linq.XName]'DataGridTextColumn')
    $column.SetAttributeValue('Header', $Header)
    if ($PropertyElement) {
        $property = [Xml.Linq.XElement]::new([Xml.Linq.XName]'DataGridTextColumn.Binding')
        $bindingElement = [Xml.Linq.XElement]::new([Xml.Linq.XName]'Binding')
        $bindingElement.SetAttributeValue('Path', $Binding)
        $property.Add($bindingElement); $column.Add($property)
    } else { $column.SetAttributeValue('Binding', $Binding) }
    return $column
}
function Observe-Columns([Xml.Linq.XElement[]]$Columns) {
    $grid = [Xml.Linq.XElement]::new([Xml.Linq.XName]'Grid')
    foreach ($column in $Columns) { $grid.Add($column) }
    $view = [Xml.Linq.XDocument]::new($grid)
    [pscustomobject]@{ rowBindings=@(
        [PlanRowBindingInspector]::Inspect($view, 'Views/PlanWorkspaceView.xaml')
        [PlanRowBindingInspector]::Inspect($view, 'Views/PlanView.xaml')
    ) }
}
function Invoke-FixtureGit([string[]]$Arguments) {
    $output = & git -C $fixture @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Fixture Git command failed: $output" }
}
function Put-Fixture([string]$Relative, [string]$Content) {
    $path = Join-Path $fixture $Relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $Content, $utf8)
}

$originals = @{
    R02 = "SELECT * FROM dbo.Users WHERE LTRIM(UserName) = 'admin';"
    R19 = 'DECLARE @T TABLE (id int); BEGIN TRANSACTION; INSERT INTO @T VALUES (1); ROLLBACK TRANSACTION; SELECT COUNT(*) FROM @T;'
    R20 = 'CREATE TABLE #T (id int); DECLARE @T TABLE (id int); SELECT * FROM @T;'
}
foreach ($id in @('R02','R19','R20')) {
    Check "$id rejects failed fallback to original SQL" {
        $o=New-RefactorObservation $originals[$id]; $o.IsSuccess=$false
        Assert-True ((Get-RefactorProbeCheck $id $o).status -eq 'ProbeExecutionFailed') 'Failed fallback was accepted.'
    }
    Check "$id accepts successful unchanged SQL" {
        Assert-True ((Get-RefactorProbeCheck $id (New-RefactorObservation $originals[$id])).status -eq 'ProbeConditionMet') 'Safe unchanged SQL was rejected.'
    }
    Check "$id rejects swallowed rule failure even when IsSuccess is true" {
        $o=New-RefactorObservation $originals[$id]; $o.ruleFailures=@([pscustomobject]@{RuleId='synthetic'; ExceptionName='InvalidOperationException'})
        Assert-True ((Get-RefactorProbeCheck $id $o).status -eq 'ProbeExecutionFailed') 'Rule failure was accepted.'
    }
}
Check 'R02 keeps successful unsafe rewriting NotMet' {
    $o=New-RefactorObservation "SELECT * FROM dbo.Users WHERE UserName='admin';"; $o.changeCount=1
    Assert-True ((Get-RefactorProbeCheck 'R02' $o).status -eq 'NotMet') 'Unsafe rewrite was accepted.'
}
Check 'R19 keeps unsafe table conversion NotMet' {
    Assert-True ((Get-RefactorProbeCheck 'R19' (New-RefactorObservation 'CREATE TABLE #T(id int);')).status -eq 'NotMet') 'Conversion was accepted.'
}
Check 'R20 rejects duplicate creation and caller-table cleanup' {
    foreach ($sql in @('CREATE TABLE #T(id int); CREATE TABLE #T(id int);','CREATE TABLE #T(id int); DROP TABLE #T;')) {
        Assert-True ((Get-RefactorProbeCheck 'R20' (New-RefactorObservation $sql)).status -eq 'NotMet') 'Collision/cleanup was accepted.'
    }
}
Check 'Refactor parser errors cannot pass' {
    $o=New-RefactorObservation $originals.R02; $o.parseErrorCount=1
    Assert-True ((Get-RefactorProbeCheck 'R02' $o).status -eq 'ProbeExecutionFailed') 'Parser errors were accepted.'
}
Check 'Keywords in comments or literals cannot pass a risk guard' {
    foreach ($sql in @("SELECT 'LTRIM(UserName)';", "SELECT UserName FROM Users; -- LTRIM(UserName)")) {
        Assert-True ((Get-RefactorProbeCheck 'R02' (New-RefactorObservation $sql)).status -eq 'NotMet') 'Keyword-only evidence passed.'
    }
}
Check 'A preserved SQL without visible skip prerequisites is NotMet' {
    $o=New-RefactorObservation $originals.R19; $o.safetySkips=@()
    Assert-True ((Get-RefactorProbeCheck 'R19' $o).status -eq 'NotMet') 'Missing safety skip was accepted.'
    $o=New-RefactorObservation $originals.R20; $o.warnings=@()
    Assert-True ((Get-RefactorProbeCheck 'R20' $o).status -eq 'NotMet') 'Invisible warning was accepted.'
}
Check 'Scalar result collections are invalid infrastructure data' {
    $o=New-RefactorObservation $originals.R02; $o.Errors=''
    Assert-Throws { Get-RefactorProbeCheck 'R02' $o }
}
Check 'Refactor error collection cannot pass' {
    $o=New-RefactorObservation $originals.R02; $o.Errors=@('Validation failed')
    Assert-True ((Get-RefactorProbeCheck 'R02' $o).status -eq 'ProbeExecutionFailed') 'Error collection was ignored.'
}
Check 'Malformed observation is an infrastructure error' {
    $o=New-RefactorObservation $originals.R02; $o.IsSuccess='false'
    Assert-Throws { Get-RefactorProbeCheck 'R02' $o }
    $o=New-RefactorObservation $originals.R02; $o.PSObject.Properties.Remove('Errors')
    Assert-Throws { Get-RefactorProbeCheck 'R02' $o }
}
Check 'R21 accepts separate ActRows and ActRowsRead columns' {
    $o=Observe-Columns @((New-Column '实际行 (ActRows)' '{Binding ActualRows}'),(New-Column '实际读取行 (ActRowsRead)' '{Binding ActualRowsRead}'))
    Assert-True (Test-PlanRowBindings $o) 'Correct output/read columns were rejected.'
}
Check 'R21 accepts explicit Path with additional formatting' {
    $o=Observe-Columns @((New-Column 'ActRows' '{Binding Path=ActualRows, StringFormat={}{0:N0}}'),(New-Column 'ActRowsRead' '{Binding Path=ActualRowsRead, Mode=OneWay}'))
    Assert-True (Test-PlanRowBindings $o) 'Explicit Binding.Path was rejected.'
}
Check 'R21 accepts property-element bindings and Chinese headers' {
    $o=Observe-Columns @((New-Column '实际输出行' 'ActualRows' -PropertyElement),(New-Column '实际读取行' 'ActualRowsRead' -PropertyElement))
    Assert-True (Test-PlanRowBindings $o) 'Property-element bindings were rejected.'
}
Check 'R21 rejects the original incorrect binding' {
    $o=Observe-Columns @((New-Column '实际行 (ActRows)' '{Binding ActualRowsRead}'),(New-Column 'ActRowsRead' '{Binding ActualRowsRead}'))
    Assert-True (-not (Test-PlanRowBindings $o)) 'Wrong output binding was accepted.'
}
Check 'R21 rejects swapped output/read bindings' {
    $o=Observe-Columns @((New-Column 'ActRows' '{Binding ActualRowsRead}'),(New-Column 'ActRowsRead' '{Binding ActualRows}'))
    Assert-True (-not (Test-PlanRowBindings $o)) 'Swapped bindings were accepted.'
}
Check 'R21 rejects missing and duplicate output columns' {
    $read=New-Column 'ActRowsRead' '{Binding ActualRowsRead}'
    Assert-True (-not (Test-PlanRowBindings (Observe-Columns @($read)))) 'Read column was treated as output.'
    $o=Observe-Columns @((New-Column 'ActRows' '{Binding ActualRows}'),(New-Column 'ActRows' '{Binding ActualRows}'),$read)
    Assert-True (-not (Test-PlanRowBindings $o)) 'Duplicate output columns were accepted.'
}
Check 'R21 rejects unsupported bindings and ignores nested Path text' {
    foreach ($binding in @('{DynamicResource ActualRows}','{Binding Other, StringFormat=''Path=ActualRows, value''}','{Binding ActualRowsRead, Path=ActualRows}')) {
        $o=Observe-Columns @((New-Column 'ActRows' $binding),(New-Column 'ActRowsRead' '{Binding ActualRowsRead}'))
        Assert-True (-not (Test-PlanRowBindings $o)) 'Unsupported/ambiguous binding was accepted.'
    }
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SqlXmlAnalyzer-IMP02-Regressions-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
try {
    Check 'Process timeout preserves partial output and a failed command record' {
        $stem=Join-Path $fixture 'timeout'
        $r=Invoke-AcceptanceProcess 'pwsh' @('-NoProfile','-Command','[Console]::Out.WriteLine("before-timeout"); Start-Sleep -Seconds 60') $fixture $stem 2000
        Assert-True $r.timedOut 'Timeout was not recorded.'
        Assert-True ([IO.File]::ReadAllText("$stem.stdout.txt").Contains('before-timeout')) 'Partial output was lost.'
        Assert-True (Test-Path -LiteralPath "$stem.command.json") 'Command evidence was lost.'
        Assert-True ($null -eq (Get-Process -Id $r.processId -ErrorAction SilentlyContinue)) 'Timed-out process was left running.'
    }
    Check 'Missing executable leaves error evidence instead of successful exit' {
        $stem=Join-Path $fixture 'missing-process'
        $r=Invoke-AcceptanceProcess (Join-Path $fixture 'absent.exe') @() $fixture $stem
        Assert-True ($r.exitCode -eq -1 -and $null -ne $r.failure -and -not $r.outputComplete) 'Startup failure was accepted.'
        Assert-True (Test-Path -LiteralPath "$stem.command.json") 'Startup failure has no record.'
    }
    Check 'Both build policies apply to acceptance logs' {
        foreach ($mode in @('Debug','Release')) {
            $path=Join-Path $fixture "$mode.log"
            foreach ($level in @('DEBUG','WARN','ERROR','CRITICAL')) { Write-AcceptanceLog $path $mode $level 'synthetic-marker' }
            $text=[IO.File]::ReadAllText($path)
            Assert-True ($text.Contains('[ERROR]') -and $text.Contains('[CRITICAL]')) 'Required error/fatal logs missing.'
            Assert-True ($text.Contains('[DEBUG]') -eq ($mode -eq 'Debug') -and $text.Contains('[WARN]') -eq ($mode -eq 'Debug')) 'Wrong build log levels.'
        }
    }
    Invoke-FixtureGit @('-c','init.defaultBranch=imp02-test','init','--quiet')
    Put-Fixture 'Existing.cs' '// baseline'
    Put-Fixture 'Deleted.cs' '// deletable'
    Put-Fixture 'publish.ps1' '# source, not generated publish output'
    Put-Fixture '.gitignore' "bin/`nobj/`n"
    Invoke-FixtureGit @('add','Existing.cs','Deleted.cs','publish.ps1','.gitignore')
    Invoke-FixtureGit @('-c','user.name=IMP02 Regression','-c','user.email=imp02@example.invalid','commit','--quiet','-m','Synthetic test baseline')
    $baseline=Get-AcceptanceInputSnapshot $fixture
    Check 'Unchanged input snapshot has no drift' {
        Assert-True (@(Compare-AcceptanceInputSnapshot $baseline (Get-AcceptanceInputSnapshot $fixture)).Count -eq 0) 'False input drift.'
    }
    Check 'Untracked source addition is detected' {
        Put-Fixture 'NewRule.cs' '// added'
        try { Assert-True (@(Compare-AcceptanceInputSnapshot $baseline (Get-AcceptanceInputSnapshot $fixture) | Where-Object { $_.kind -eq 'Added' -and $_.path -eq 'NewRule.cs' }).Count -eq 1) 'New source was missed.' }
        finally { Remove-Item -LiteralPath (Join-Path $fixture 'NewRule.cs') }
    }
    Check 'Staged source addition is detected' {
        Put-Fixture 'Added.xaml' '<Grid />'; Invoke-FixtureGit @('add','Added.xaml')
        try { Assert-True (@(Compare-AcceptanceInputSnapshot $baseline (Get-AcceptanceInputSnapshot $fixture) | Where-Object kind -EQ 'Added').Count -eq 1) 'Staged source was missed.' }
        finally { Invoke-FixtureGit @('rm','--cached','--quiet','Added.xaml'); Remove-Item -LiteralPath (Join-Path $fixture 'Added.xaml') }
    }
    Check 'New configuration is detected' {
        Put-Fixture 'NewRules.json' '{}'
        try { Assert-True (@(Compare-AcceptanceInputSnapshot $baseline (Get-AcceptanceInputSnapshot $fixture) | Where-Object path -EQ 'NewRules.json').Count -eq 1) 'New config was missed.' }
        finally { Remove-Item -LiteralPath (Join-Path $fixture 'NewRules.json') }
    }
    Check 'Tracked deletion is detected without a hash exception' {
        Remove-Item -LiteralPath (Join-Path $fixture 'Deleted.cs')
        try { Assert-True (@(Compare-AcceptanceInputSnapshot $baseline (Get-AcceptanceInputSnapshot $fixture) | Where-Object kind -EQ 'Deleted').Count -eq 1) 'Deletion was missed.' }
        finally { Put-Fixture 'Deleted.cs' '// deletable' }
    }
    Check 'Content changes including publish.ps1 are detected' {
        Put-Fixture 'publish.ps1' '# changed'
        try { Assert-True (@(Compare-AcceptanceInputSnapshot $baseline (Get-AcceptanceInputSnapshot $fixture) | Where-Object { $_.kind -eq 'ContentChanged' -and $_.path -eq 'publish.ps1' }).Count -eq 1) 'Publish script was incorrectly excluded.' }
        finally { Put-Fixture 'publish.ps1' '# source, not generated publish output' }
    }
    Check 'Generated directories and new evidence do not trigger source drift' {
        foreach ($path in @('bin/Generated.cs','obj/Generated.cs','.vs/Generated.cs','publish-x64/Generated.cs','publish/Generated.cs','backups/Generated.cs','.tmp.test/Generated.cs','DOCS/runs/new.json')) { Put-Fixture $path 'generated' }
        Assert-True (@(Compare-AcceptanceInputSnapshot $baseline (Get-AcceptanceInputSnapshot $fixture)).Count -eq 0) 'Generated files triggered drift.'
    }
    Check 'HEAD changes are detected even with identical file contents' {
        Invoke-FixtureGit @('-c','user.name=IMP02 Regression','-c','user.email=imp02@example.invalid','commit','--allow-empty','--quiet','-m','Synthetic HEAD change')
        Assert-True (@(Compare-AcceptanceInputSnapshot $baseline (Get-AcceptanceInputSnapshot $fixture) | Where-Object kind -EQ 'HeadChanged').Count -eq 1) 'HEAD change was missed.'
    }
    Check 'Compilation copy uses frozen bytes after the live source changes' {
        Put-Fixture 'live.cs.txt' 'original probe bytes'
        $frozen=Join-Path $fixture 'frozen.cs.txt'; $target=Join-Path $fixture 'compiled.cs.txt'
        [IO.File]::Copy((Join-Path $fixture 'live.cs.txt'),$frozen,$false)
        $hash=(Get-FileHash -LiteralPath $frozen -Algorithm SHA256).Hash
        Put-Fixture 'live.cs.txt' 'edited during build'
        Copy-FrozenAcceptanceInput $frozen $target $hash
        Assert-True ([IO.File]::ReadAllText($target) -ceq 'original probe bytes') 'Mutable live source was compiled.'
        Assert-Throws { Copy-FrozenAcceptanceInput $frozen $target $hash }
    }
    Check 'Tampered snapshot is rejected before compilation copy' {
        $frozen=Join-Path $fixture 'frozen.cs.txt'; $hash=(Get-FileHash -LiteralPath $frozen -Algorithm SHA256).Hash
        Put-Fixture 'frozen.cs.txt' 'tampered'
        Assert-Throws { Copy-FrozenAcceptanceInput $frozen (Join-Path $fixture 'must-not-compile.cs.txt') $hash }
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture 'must-not-compile.cs.txt'))) 'Tampered input reached compilation.'
    }
} finally {
    # Delete only this test-created directory after verifying its resolved absolute location.
    $resolved=(Resolve-Path -LiteralPath $fixture).Path
    $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolved)).StartsWith('SqlXmlAnalyzer-IMP02-Regressions-', [StringComparison]::Ordinal)) {
        throw 'Refusing to clean an unexpected regression fixture path.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$record=[ordered]@{
    schemaVersion=1; checkedUtc=[DateTimeOffset]::UtcNow.ToString('o')
    total=$results.Count; passed=@($results | Where-Object status -EQ 'Passed').Count; failed=@($results | Where-Object status -EQ 'Failed').Count
    tests=$results.ToArray(); sqlServerExecuted=$false; wpfVisualTestPerformed=$false
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath)))
[IO.File]::WriteAllText($OutputPath,($record | ConvertTo-Json -Depth 8),$utf8)
$record | ConvertTo-Json -Depth 8
if ($record.failed -gt 0) { exit 1 }
