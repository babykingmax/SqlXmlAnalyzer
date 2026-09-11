param([string]$Server = '(localdb)\SqlXmlAnalyzer_IMP19_2019')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path $repository '.tmp.imp26-real'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$database = 'IMP26_' + [Guid]::NewGuid().ToString('N').Substring(0,12)
$session = $database + '_XE'
$target = (Join-Path $env:USERPROFILE ($database + '.xel'))
$version = ((& sqlcmd -S $Server -E -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(nvarchar(30),SERVERPROPERTY('ProductVersion'));") -join '').Trim()
function Execute([string]$Sql) {
    $command = Join-Path $output ('command-' + [Guid]::NewGuid().ToString('N') + '.sql')
    $Sql | Set-Content -LiteralPath $command -Encoding UTF8
    & sqlcmd -S $Server -E -b -t 30 -i $command >> (Join-Path $output 'capture.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'SQL fixture command failed; see local capture.log' }
}
try {
    Execute "CREATE DATABASE [$database];"
    Execute "USE [$database]; CREATE TABLE dbo.DeadlockFixture(Id int PRIMARY KEY, Value int NOT NULL); INSERT dbo.DeadlockFixture VALUES(1,0),(2,0);"
    Execute @"
CREATE EVENT SESSION [$session] ON SERVER
ADD EVENT sqlserver.xml_deadlock_report,
ADD EVENT sqlserver.sql_batch_completed(ACTION(sqlserver.database_name) WHERE ([sqlserver].[database_name]=N'$database'))
ADD TARGET package0.event_file(SET filename=N'$target',max_file_size=(5),max_rollover_files=(4))
WITH(MAX_DISPATCH_LATENCY=1 SECONDS);
ALTER EVENT SESSION [$session] ON SERVER STATE=START;
"@
    "USE [$database]; SET DEADLOCK_PRIORITY LOW; BEGIN TRAN; UPDATE dbo.DeadlockFixture SET Value=Value+1 WHERE Id=1; WAITFOR DELAY '00:00:03'; UPDATE dbo.DeadlockFixture SET Value=Value+1 WHERE Id=2; COMMIT;" | Set-Content -LiteralPath (Join-Path $output 'a.sql')
    "USE [$database]; BEGIN TRAN; UPDATE dbo.DeadlockFixture SET Value=Value+1 WHERE Id=2; WAITFOR DELAY '00:00:03'; UPDATE dbo.DeadlockFixture SET Value=Value+1 WHERE Id=1; COMMIT;" | Set-Content -LiteralPath (Join-Path $output 'b.sql')
    foreach ($iteration in 1..2) {
        $processes = foreach ($side in 'a','b') {
            Start-Process -FilePath (Get-Command sqlcmd).Source -ArgumentList @('-S',$Server,'-E','-i',(Join-Path $output "$side.sql")) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output "$side-$iteration.log")
        }
        $processes | Wait-Process
    }
    Execute "ALTER EVENT SESSION [$session] ON SERVER STATE=STOP;"
    $file = Get-ChildItem -LiteralPath (Split-Path $target) -Filter ($database + '*.xel') -File | Select-Object -First 1
    $destination = Join-Path $output 'multiple.xel'
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    Set-Content -LiteralPath (Join-Path $output 'input-path.txt') -Value $destination
    @{ FileName=$file.Name; Length=$file.Length; Sha256=(Get-FileHash -LiteralPath $file.FullName).Hash; Database=$database; SqlVersion=$version; DeadlockCycles=2 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'xel-fixture.json')
    Remove-Item -LiteralPath $file.FullName
}
finally {
    Execute "IF EXISTS(SELECT 1 FROM sys.server_event_sessions WHERE name=N'$session') DROP EVENT SESSION [$session] ON SERVER; IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END;"
}
