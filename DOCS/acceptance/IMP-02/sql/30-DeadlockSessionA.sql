-- Run A and B in separate disposable test sessions within the 5-second window.
USE [SqlXmlAnalyzer_Imp02_Acceptance];
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'SqlXmlAnalyzer.IMP02' AND CONVERT(nvarchar(128),value)=N'SyntheticAcceptanceFixture-v1')
    THROW 51000, 'Missing acceptance fixture ownership marker.', 1;
IF @@TRANCOUNT<>0 THROW 51000, 'Use a fresh session.', 1;
SET LOCK_TIMEOUT 15000;
SET DEADLOCK_PRIORITY NORMAL;
BEGIN TRY
    BEGIN TRANSACTION;
    UPDATE dbo.Imp02LockFixture SET CounterValue=CounterValue+1 WHERE Id=1;
    WAITFOR DELAY '00:00:05';
    UPDATE dbo.Imp02LockFixture SET CounterValue=CounterValue+1 WHERE Id=2;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
END CATCH;
