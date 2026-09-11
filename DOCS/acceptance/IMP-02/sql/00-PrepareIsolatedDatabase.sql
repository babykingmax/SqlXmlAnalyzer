-- PREPARATION ONLY. Run deliberately on a disposable SQL Server test instance.
-- This single batch refuses an existing database; it never reuses production data.
USE [master];
SET NOCOUNT ON;
IF @@TRANCOUNT <> 0 THROW 51000, 'Run outside an existing transaction.', 1;
IF DB_ID(N'SqlXmlAnalyzer_Imp02_Acceptance') IS NOT NULL
    THROW 51000, 'Database already exists. Inspect it; do not overwrite it.', 1;
EXEC(N'CREATE DATABASE [SqlXmlAnalyzer_Imp02_Acceptance];');
EXEC(N'USE [SqlXmlAnalyzer_Imp02_Acceptance];
EXEC sys.sp_addextendedproperty @name=N''SqlXmlAnalyzer.IMP02'', @value=N''SyntheticAcceptanceFixture-v1'';
CREATE TABLE dbo.Users(CaseId int NOT NULL PRIMARY KEY, UserName nvarchar(40) NULL);
INSERT dbo.Users VALUES (1,N''admin''),(2,N'' admin''),(3,NULL),(4,N''   ''),(5,N''other'');
CREATE TABLE dbo.Imp02LockFixture(Id int NOT NULL PRIMARY KEY, CounterValue int NOT NULL);
INSERT dbo.Imp02LockFixture VALUES(1,0),(2,0);
CREATE TABLE dbo.[Order.Detail](K int NOT NULL, V int NULL);
INSERT dbo.[Order.Detail] VALUES (1,10);
CREATE TABLE dbo.Imp02PlanData(Id int NOT NULL PRIMARY KEY, K int NOT NULL, Payload char(80) NOT NULL);
WITH Digits AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n))
INSERT dbo.Imp02PlanData(Id,K,Payload)
SELECT 1+a.n+10*b.n+100*c.n+1000*d.n, (a.n+10*b.n)%20, REPLICATE(''X'',80)
FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c CROSS JOIN Digits d;
CREATE INDEX IX_Imp02PlanData_K ON dbo.Imp02PlanData(K);
');
SELECT N'Prepared synthetic database; no product SQL has been applied.' AS Result;
