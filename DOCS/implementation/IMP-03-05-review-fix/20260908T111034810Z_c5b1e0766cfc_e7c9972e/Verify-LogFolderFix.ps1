[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $repositoryPath 'SqlXmlAnalyzer.sln') -PathType Leaf)) {
    throw 'The repository must contain SqlXmlAnalyzer.sln.'
}
$runName = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '_' + [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path $PSScriptRoot ('reruns/' + $runName)
New-Item -ItemType Directory -Path $runDirectory -ErrorAction Stop | Out-Null
$outcomes = [System.Collections.Generic.List[object]]::new()
$completed = $false
Push-Location -LiteralPath $repositoryPath
try {
    $sourcePaths = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.cs' '*.csproj' '*.props' '*.xaml' '*.sln' 'RuleConfiguration.json')
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    $fingerprints = @($sourcePaths | Sort-Object -Unique | ForEach-Object {
        [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    })
    [pscustomobject]@{
        Head = (git rev-parse HEAD).Trim()
        Sdk = (dotnet --version).Trim()
        Utc = [DateTime]::UtcNow.ToString('O')
        SourceFiles = $fingerprints
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDirectory 'inputs.json') -Encoding utf8

    foreach ($configuration in @('Debug', 'Release')) {
        $buildArguments = @('build', 'SqlXmlAnalyzer.sln', '-c', $configuration, '--nologo')
        & dotnet @buildArguments *> (Join-Path $runDirectory ($configuration + '-build.log'))
        $buildExitCode = $LASTEXITCODE
        $outcomes.Add([pscustomobject]@{ Kind = 'Build'; Configuration = $configuration; Arguments = $buildArguments; ExitCode = $buildExitCode })
        if ($buildExitCode -ne 0) { throw "$configuration build failed; see $runDirectory" }

        $resultsDirectory = Join-Path $runDirectory $configuration
        $testArguments = @('test', 'SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj', '-c', $configuration,
            '--no-build', '--no-restore', '--logger', 'trx;LogFileName=full.trx', '--results-directory', $resultsDirectory)
        & dotnet @testArguments *> (Join-Path $runDirectory ($configuration + '-test.log'))
        $testExitCode = $LASTEXITCODE
        $outcomes.Add([pscustomobject]@{ Kind = 'Test'; Configuration = $configuration; Arguments = $testArguments; ExitCode = $testExitCode })
        if ($testExitCode -ne 0) { throw "$configuration tests failed; see $runDirectory" }
    }
    $completed = $true
}
finally {
    [pscustomobject]@{ Completed = $completed; Results = $outcomes.ToArray() } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding utf8
    Pop-Location
}
Write-Output $runDirectory
