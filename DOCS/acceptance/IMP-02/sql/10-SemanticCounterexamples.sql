-- Reference semantic counterexamples, NOT an automatic equivalence proof for generated candidates.
-- Run in a fresh query session after 00. Save all result sets and session metadata.
USE [SqlXmlAnalyzer_Imp02_Acceptance];
SET NOCOUNT ON;
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'SqlXmlAnalyzer.IMP02' AND CONVERT(nvarchar(128),value)=N'SyntheticAcceptanceFixture-v1')
    THROW 51000, 'Missing acceptance fixture ownership marker.', 1;
IF @@TRANCOUNT <> 0 THROW 51000, 'Use a fresh session without an active transaction.', 1;
IF OBJECT_ID('tempdb..#Imp02Rollback') IS NOT NULL OR OBJECT_ID('tempdb..#Imp02Savepoint') IS NOT NULL OR OBJECT_ID('tempdb..#T') IS NOT NULL
    THROW 51000, 'Temporary names already exist; use a fresh session.', 1;
SELECT @@VERSION AS ServerVersion, DB_NAME() AS DatabaseName,
       compatibility_level, collation_name FROM sys.databases WHERE database_id=DB_ID();
DBCC USEROPTIONS;

-- R02: compare row identities, not only total counts. Original should include CaseId 2.
SELECT N'R02-original' AS Scenario, CaseId FROM dbo.Users WHERE LTRIM(UserName)=N'admin' ORDER BY CaseId;
SELECT N'R02-reference-mutation' AS Scenario, CaseId FROM dbo.Users WHERE UserName=N'admin' ORDER BY CaseId;

-- R19: declarations precede the transaction. Expected reference observations: 1 versus 0.
DECLARE @Imp02Original TABLE(Id int);
BEGIN TRANSACTION;
INSERT @Imp02Original VALUES(1);
ROLLBACK TRANSACTION;
SELECT N'R19-original-rollback' AS Scenario, COUNT(*) AS RowsAfterRollback FROM @Imp02Original;
CREATE TABLE #Imp02Rollback(Id int);
BEGIN TRANSACTION;
INSERT #Imp02Rollback VALUES(1);
ROLLBACK TRANSACTION;
SELECT N'R19-reference-temp-rollback' AS Scenario, COUNT(*) AS RowsAfterRollback FROM #Imp02Rollback;
DROP TABLE #Imp02Rollback;

-- SAVEPOINT is a separate case; do not infer it was verified by the full rollback case.
DECLARE @Imp02Save TABLE(Id int);
BEGIN TRANSACTION;
INSERT @Imp02Save VALUES(1);
SAVE TRANSACTION Imp02Save;
INSERT @Imp02Save VALUES(2);
ROLLBACK TRANSACTION Imp02Save;
COMMIT TRANSACTION;
SELECT N'R19-original-savepoint' AS Scenario, COUNT(*) AS RowsAfterSavepoint FROM @Imp02Save;
CREATE TABLE #Imp02Savepoint(Id int);
BEGIN TRANSACTION;
INSERT #Imp02Savepoint VALUES(1);
SAVE TRANSACTION Imp02Save;
INSERT #Imp02Savepoint VALUES(2);
ROLLBACK TRANSACTION Imp02Save;
COMMIT TRANSACTION;
SELECT N'R19-reference-temp-savepoint' AS Scenario, COUNT(*) AS RowsAfterSavepoint FROM #Imp02Savepoint;
DROP TABLE #Imp02Savepoint;

-- TRY/CATCH rollback: preserve the original table variable observation and error identity.
DECLARE @Imp02Catch TABLE(Id int);
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT @Imp02Catch VALUES(1);
    THROW 51001, 'Synthetic error for rollback semantics.', 1;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    SELECT N'R19-original-catch' AS Scenario, ERROR_NUMBER() AS ErrorNumber, COUNT(*) AS RowsAfterError FROM @Imp02Catch;
END CATCH;

-- R20: malformed reference mutation. Dynamic scope makes compile errors catchable here.
CREATE TABLE #T(Id int);
INSERT #T VALUES(7);
BEGIN TRY
    EXEC sys.sp_executesql N'CREATE TABLE #T(Id int); CREATE TABLE #T(Id int);';
    SELECT N'R20-unexpected-no-error; investigate' AS Scenario;
END TRY
BEGIN CATCH
    SELECT N'R20-reference-collision' AS Scenario, ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
END CATCH;
SELECT N'R20-original-table-still-owned-by-caller' AS Scenario, Id FROM #T;
DROP TABLE #T;

-- R13: the dot belongs to one object name. DDL compiler output must target this table.
SELECT N'R13-single-identifier' AS Scenario, OBJECT_ID(N'dbo.[Order.Detail]') AS ObjectId;
