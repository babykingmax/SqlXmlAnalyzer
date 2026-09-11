-- Deliberate cleanup only after saving evidence and closing the test sessions.
-- Keep the database for review; this script never drops a database or unrelated objects.
USE [SqlXmlAnalyzer_Imp02_Acceptance];
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'SqlXmlAnalyzer.IMP02' AND CONVERT(nvarchar(128),value)=N'SyntheticAcceptanceFixture-v1')
    THROW 51000, 'Missing acceptance fixture ownership marker.', 1;
IF @@TRANCOUNT<>0 THROW 51000, 'Close or roll back the test transaction first.', 1;
DROP TABLE IF EXISTS dbo.Imp02PlanData;
DROP TABLE IF EXISTS dbo.Imp02LockFixture;
DROP TABLE IF EXISTS dbo.[Order.Detail];
DROP TABLE IF EXISTS dbo.Users;
EXEC sys.sp_dropextendedproperty @name=N'SqlXmlAnalyzer.IMP02';
SELECT N'Fixture objects removed. Database retained for deliberate review/removal.' AS Result;
