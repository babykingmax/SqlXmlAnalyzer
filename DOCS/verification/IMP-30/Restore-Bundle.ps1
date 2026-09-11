#requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$Bundle,
    [Parameter(Mandatory)][string]$ManifestSha256,
    [Parameter(Mandatory)][string]$Destination,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$null = [Reflection.Assembly]::LoadFrom((Join-Path $repository "bin/$Configuration/net8.0-windows/SqlXmlAnalyzer.Core.dll"))
$diagnostics = Join-Path $repository ('.tmp.imp30-restore-' + [Guid]::NewGuid().ToString('N'))
$code = [SqlXmlAnalyzer.Core.Release.ReleaseOperation]::Run([Action]{
    [SqlXmlAnalyzer.Core.Release.ReleaseBundle]::Restore($Bundle,$ManifestSha256,$Destination,[Threading.CancellationToken]::None)
    [Console]::WriteLine("Verified recovery copy: $Destination")
},$diagnostics)
exit $code
