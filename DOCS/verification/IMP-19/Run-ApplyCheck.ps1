param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$dir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot ('apply-' + $Configuration.ToLowerInvariant()) }
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$cli = Join-Path $repo "SqlXmlAnalyzer.CLI/bin/$Configuration/net8.0/SqlXmlAnalyzer.CLI.dll"
$source = Join-Path $dir 'source.sql'
$original = "-- reviewed fixture`r`nSELECT Id FROM dbo.Users WHERE Name = N'admin' AND Age + 10 > 50;`r`n"
[IO.File]::WriteAllText($source, $original, [Text.UTF8Encoding]::new($true))
$sourceBytes = [IO.File]::ReadAllBytes($source)
$sourceFileHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$suitePath = Join-Path $dir 'suite.json'
$suite = @{InstanceName='SqlXmlAnalyzer_IMP19_2025'; CompatibilityLevel=170; Collation='Latin1_General_100_CI_AS_SC'; Scenarios=@(@{Name='ascii-null-threshold'; SetupSql="CREATE TABLE dbo.Users(Id int, Name nvarchar(20), Age int CHECK(Age BETWEEN -100000 AND 100000)); INSERT dbo.Users VALUES(1,N'admin',40),(2,N'admin',41),(3,NULL,NULL),(4,N'other',50),(5,N' admin',100),(6,N'Admin',50);"; ObserveSql='SELECT Id, Name, Age FROM dbo.Users;'; OrderedResults=$false})}
[IO.File]::WriteAllText($suitePath, ($suite | ConvertTo-Json -Depth 6))
$initial = & dotnet $cli refactor $source --format json --show-sql 2> (Join-Path $dir 'proposal.log')
if ($LASTEXITCODE -ne 0) { throw 'Proposal generation failed' }
$proposal = ($initial -join [Environment]::NewLine) | ConvertFrom-Json
if ($proposal.Review.Proposals.Count -ne 2) { throw 'Expected two independent review steps' }
[IO.File]::WriteAllText((Join-Path $dir 'proposals.json'), ($initial -join [Environment]::NewLine))
$id = $proposal.Review.Proposals[0].Id
& dotnet $cli rewrite-validate $source --select $id --scenarios $suitePath --show-sql --output (Join-Path $dir 'validated.json') > (Join-Path $dir 'validate.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Live validation failed' }
$validated = Get-Content (Join-Path $dir 'validated.json') -Raw | ConvertFrom-Json
if (-not $validated.CanApply -or $validated.SourceWritten -or $validated.PreviewSql -notmatch 'Age \+ 10 > 50') { throw 'Selection preview incorrect' }
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceFileHash) { throw 'Validation changed source' }
$approval = @('--select',$id,'--scenarios',$suitePath,'--review-source-hash',$validated.SourceHash,'--review-preview-hash',$validated.PreviewHash,'--review-suite-hash',$validated.Validation.SuiteHash,'--acknowledge-scenarios')
& dotnet $cli rewrite-apply $source @approval --output (Join-Path $dir 'applied.json') > (Join-Path $dir 'apply.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Live apply failed' }
$applied = Get-Content (Join-Path $dir 'applied.json') -Raw | ConvertFrom-Json
if (-not $applied.SourceWritten -or -not $applied.Writeback.BackupVerified -or [IO.File]::ReadAllText($source) -cne $validated.PreviewSql) { throw 'Applied bytes differ from selected preview' }
if ((Get-FileHash -LiteralPath $applied.Writeback.BackupPath -Algorithm SHA256).Hash -ne $sourceFileHash) { throw 'Backup mismatch' }
if ([IO.File]::ReadAllBytes($source)[0] -ne 0xEF) { throw 'UTF-8 BOM lost' }
# Report failure after commit must retain the actual committed state in stdout.
$fallbackSource = Join-Path $dir 'report-failure-source.sql'
[IO.File]::WriteAllBytes($fallbackSource, $sourceBytes)
$stdout = & dotnet $cli rewrite-apply $fallbackSource @approval --output (Join-Path $dir 'missing-directory/report.json') 2> (Join-Path $dir 'report-failure.log')
if ($LASTEXITCODE -ne 1) { throw 'Expected report output failure' }
$fallback = ($stdout -join [Environment]::NewLine) | ConvertFrom-Json
if (-not $fallback.ReportOutputFailed -or -not $fallback.Result.SourceWritten -or -not $fallback.Result.Writeback.BackupVerified) { throw 'Committed state lost after report failure' }
[IO.File]::WriteAllText((Join-Path $dir 'report-failure-result.json'), ($stdout -join [Environment]::NewLine))
[IO.File]::WriteAllText((Join-Path $dir 'summary.json'), (@{Passed=$true; SourceFileHash=$sourceFileHash; SelectedIds=@($id); ExpectedPreviewHash=$validated.PreviewHash; AppliedOutputHash=$applied.Writeback.OutputSha256; BackupVerified=$applied.Writeback.BackupVerified; ReportFailureRetainsCommit=$fallback.Result.SourceWritten} | ConvertTo-Json -Depth 5))
Write-Output 'PASS: live validate/apply, selected prefix only, exact preview, BOM, backup, source unchanged before apply, report failure retains committed state.'
exit 0
