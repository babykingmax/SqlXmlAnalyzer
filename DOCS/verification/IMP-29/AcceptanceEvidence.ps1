#requires -Version 7.4
# Shared by the acceptance runners and their process-level xUnit regressions.
function Read-EvidenceJson([string]$Path) {
    # File APIs retain typed I/O failures; parse failures are expected input errors.
    $json = [IO.File]::ReadAllText($Path)
    try { ConvertFrom-Json -InputObject $json -ErrorAction Stop }
    catch [ArgumentException] { throw [IO.InvalidDataException]::new('Invalid evidence JSON.', $_.Exception) }
}

function Get-EvidenceFileHash([string]$Path) {
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
}

function Get-EvidenceHash([string]$Root, [string]$RelativePath) {
    $rootPath = [IO.Path]::GetFullPath($Root)
    $path = [IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    if (-not $path.StartsWith($rootPath.TrimEnd('/','\') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw [IO.InvalidDataException]::new("Evidence path escapes its root: $RelativePath")
    }
    $stream = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try { [pscustomobject]@{ Path=$RelativePath.Replace('\','/'); Bytes=$stream.Length; Sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } }
    finally { $stream.Dispose() }
}

function Assert-EvidenceRecords($Expected, $Actual) {
    $expectedRecords = @($Expected); $actualRecords = @($Actual)
    if ($expectedRecords.Count -eq 0 -or $expectedRecords.Count -ne $actualRecords.Count) { throw [IO.InvalidDataException]::new('Evidence inventory changed or is empty.') }
    $map = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $expectedRecords) {
        if (-not $record.Path -or $record.Sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or -not $map.TryAdd($record.Path,$record)) { throw [IO.InvalidDataException]::new('Invalid or duplicate evidence record.') }
    }
    foreach ($record in $actualRecords) {
        $saved = $null
        if (-not $map.Remove($record.Path,[ref]$saved) -or $saved.Sha256 -cne $record.Sha256 -or $saved.Bytes -ne $record.Bytes) {
            throw [IO.InvalidDataException]::new("Evidence changed: $($record.Path)")
        }
    }
}

function Assert-EvidenceFiles([string]$Root, $Records) {
    Assert-EvidenceRecords $Records @($Records | ForEach-Object { Get-EvidenceHash $Root $_.Path })
}

function Get-AcceptanceSources([string]$Repository) {
    # Start from Git's source inventory; never enumerate generated workspace trees.
    $paths = @(git -C $Repository -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.cs' '*.xaml' '*.csproj' '*.props' '*.targets' '*.sln' 'global.json' 'NuGet.config' 'RuleConfiguration.json' '.gitignore' 'publish.ps1' 'publish.bat' 'DOCS/release-materials/*.md' 'DOCS/verification/*.ps1' 'DOCS/verification/*.cs.txt' 'SqlXmlAnalyzer.Tests/TestData/*' 'SqlXmlAnalyzer.Tests/Resources/*.sqlplan' 'DOCS/verification/IMP-26/fixtures/*.sqlplan')
    if ($LASTEXITCODE -ne 0) { throw [IO.InvalidDataException]::new('Cannot inventory acceptance sources.') }
    $paths = @($paths | Where-Object { $_ -notmatch '(^|/)(\.git|bin|obj|\.vs|publish-[^/]*|backups|\.tmp\.[^/]*)(/|$)' } | Sort-Object -Unique)
    if ($paths.Count -eq 0) { throw [IO.InvalidDataException]::new('No acceptance sources found.') }
    @($paths | ForEach-Object { Get-EvidenceHash $Repository $_ })
}

function Get-AcceptanceBinaries([string]$Repository, [string]$Configuration) {
    $common = @('SqlXmlAnalyzer.Core.dll','SqlXmlAnalyzer.Analysis.dll','SqlXmlAnalyzer.Application.dll','SqlXmlAnalyzer.Refactoring.dll')
    $targets = @(
        @{Directory="bin/$Configuration/net8.0-windows"; Names=@('SqlXmlAnalyzer.dll') + $common},
        @{Directory="SqlXmlAnalyzer.CLI/bin/$Configuration/net8.0"; Names=@('SqlXmlAnalyzer.CLI.dll') + $common},
        @{Directory="SqlXmlAnalyzer.Tests/bin/$Configuration/net8.0-windows"; Names=@('SqlXmlAnalyzer.Tests.dll','SqlXmlAnalyzer.dll','SqlXmlAnalyzer.CLI.dll') + $common}
    )
    @($(foreach ($target in $targets) { foreach ($name in $target.Names) { Get-EvidenceHash $Repository ($target.Directory + '/' + $name) } }))
}

function Write-NewEvidenceJson([string]$Path, $Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 20))
    $stream = [IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
}

function Invoke-StrictBuild([string]$Project, [string]$Configuration, [string]$LogPath, [string[]]$AdditionalArguments = @()) {
    $arguments = @('build',$Project,'-c',$Configuration,'--no-restore','-m:1','-t:Rebuild','-warnaserror','-v','minimal') + $AdditionalArguments
    & dotnet @arguments > $LogPath 2>&1
    $code = $LASTEXITCODE
    $receipt = [pscustomobject]@{ ExitCode=$code; WarningsAsErrors=$true; Arguments=$arguments; CompletedAtUtc=[DateTime]::UtcNow.ToString('O') }
    Write-NewEvidenceJson ($LogPath + '.json') $receipt
    Assert-StrictBuild $receipt
    $receipt
}

function Assert-StrictBuild($Receipt) {
    if ($null -eq $Receipt.ExitCode -or $Receipt.ExitCode -ne 0 -or $Receipt.WarningsAsErrors -isnot [bool] -or -not $Receipt.WarningsAsErrors -or @($Receipt.Arguments) -notcontains '-warnaserror' -or @($Receipt.Arguments) -notcontains '-t:Rebuild') {
        throw [IO.InvalidDataException]::new('Build failed or did not enforce warnings as errors.')
    }
}

function Read-AcceptedBuild([string]$Repository, [string]$BuildEvidence) {
    $build = Read-EvidenceJson $BuildEvidence
    if ($build.SchemaVersion -ne 1 -or $build.Passed -isnot [bool] -or -not $build.Passed -or @($build.Modes).Count -ne 2 -or
        (@($build.Modes.Configuration | Sort-Object -Unique) -join ',') -ne 'Debug,Release') { throw [IO.InvalidDataException]::new('Incomplete build attestation.') }
    Assert-EvidenceRecords $build.Sources (Get-AcceptanceSources $Repository)
    foreach ($mode in $build.Modes) {
        Assert-StrictBuild $mode.Build
        Assert-EvidenceRecords $mode.Binaries (Get-AcceptanceBinaries $Repository $mode.Configuration)
    }
    Assert-EvidenceFiles (Split-Path -Parent $BuildEvidence) $build.Results
    $build
}

function Start-AcceptanceStage([string]$Repository, [string]$BuildEvidence) {
    $hash = (Get-EvidenceFileHash $BuildEvidence)
    $null = Read-AcceptedBuild $Repository $BuildEvidence
    [pscustomobject]@{ Repository=$Repository; BuildEvidence=[IO.Path]::GetFullPath($BuildEvidence); BuildSha256=$hash; StartedAtUtc=[DateTime]::UtcNow.ToString('O') }
}

function Complete-AcceptanceStage($Context, [string]$Output, [string[]]$Results) {
    if ((Get-EvidenceFileHash $Context.BuildEvidence) -ne $Context.BuildSha256) { throw [IO.InvalidDataException]::new('Build attestation changed during stage.') }
    $null = Read-AcceptedBuild $Context.Repository $Context.BuildEvidence
    $records = @($Results | Sort-Object -Unique | ForEach-Object { Get-EvidenceHash $Output $_ })
    if ($records.Count -eq 0) { throw [IO.InvalidDataException]::new('Stage produced no evidence.') }
    Write-NewEvidenceJson (Join-Path $Output 'stage-evidence.json') ([ordered]@{
        SchemaVersion=1; Passed=$true; BuildSha256=$Context.BuildSha256; StartedAtUtc=$Context.StartedAtUtc; CompletedAtUtc=[DateTime]::UtcNow.ToString('O'); Results=$records
    })
}

function Assert-AcceptanceStage([string]$Output, [string]$BuildSha256, [string[]]$RequiredResults) {
    $stage = Read-EvidenceJson (Join-Path $Output 'stage-evidence.json')
    if ($stage.SchemaVersion -ne 1 -or $stage.Passed -isnot [bool] -or -not $stage.Passed -or $stage.BuildSha256 -ne $BuildSha256) { throw [IO.InvalidDataException]::new('Stage belongs to a different or missing accepted build.') }
    foreach ($required in $RequiredResults) { if (@($stage.Results.Path) -notcontains $required) { throw [IO.InvalidDataException]::new("Unbound stage result: $required") } }
    Assert-EvidenceFiles $Output $stage.Results
    $stage
}

function Assert-AcceptanceDump([string]$CoreAssembly, [string]$DumpPath) {
    # Debug and Release have the same assembly identity. Do not load them into
    # PowerShell's shared default context, which cannot host both versions.
    $context = [Runtime.Loader.AssemblyLoadContext]::new('IMP29-Dump-' + [Guid]::NewGuid().ToString('N'), $true)
    try {
        $core = $context.LoadFromAssemblyPath([IO.Path]::GetFullPath($CoreAssembly))
        $validator = $core.GetType('SqlXmlAnalyzer.Core.Diagnostics.MinidumpValidator').GetMethod('Validate',[Reflection.BindingFlags]'NonPublic,Static',$null,[type[]]@([string]),$null)
        $arguments = [object[]]::new(1); $arguments[0] = [string][IO.Path]::GetFullPath($DumpPath)
        $null = $validator.Invoke($null,$arguments)
    }
    finally { $context.Unload() }
}
