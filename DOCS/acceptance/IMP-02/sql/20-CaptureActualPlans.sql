-- Save each returned XML as a separate .sqlplan. Record hash and source metadata.
-- MAXDOP permits a shape; it does not prove that SQL Server chose parallelism.
USE [SqlXmlAnalyzer_Imp02_Acceptance];
SET NOCOUNT ON;
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'SqlXmlAnalyzer.IMP02' AND CONVERT(nvarchar(128),value)=N'SyntheticAcceptanceFixture-v1')
    THROW 51000, 'Missing acceptance fixture ownership marker.', 1;
SELECT @@VERSION AS ServerVersion, compatibility_level, collation_name
FROM sys.databases WHERE database_id=DB_ID();
DBCC USEROPTIONS;
SET STATISTICS XML ON;
SELECT SUM(Id) AS SerialAggregate FROM dbo.Imp02PlanData WHERE K=1 OPTION(MAXDOP 1);
SELECT K, SUM(CONVERT(bigint,Id)*Id) AS ParallelCandidate
FROM dbo.Imp02PlanData GROUP BY K OPTION(MAXDOP 4);
SELECT TOP(50) a.Id, b.Payload FROM dbo.Imp02PlanData a
INNER JOIN dbo.Imp02PlanData b ON b.K=a.K WHERE a.Id<5 OPTION(LOOP JOIN,MAXDOP 1);
-- A multi-statement input must preserve both statements and their original NodeIds.
SELECT COUNT(*) AS StatementOne FROM dbo.Imp02PlanData WHERE K=2;
SELECT SUM(CONVERT(bigint,Id)) AS StatementTwo FROM dbo.Imp02PlanData WHERE K=3;
SET STATISTICS XML OFF;
