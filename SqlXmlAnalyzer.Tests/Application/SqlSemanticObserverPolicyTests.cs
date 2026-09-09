using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class SqlSemanticObserverPolicyTests
{
    [Theory]
    [InlineData("DELETE FROM dbo.T;")]
    [InlineData("UPDATE dbo.T SET Id=0;")]
    [InlineData("INSERT dbo.T VALUES(1);")]
    [InlineData("DROP TABLE dbo.T;")]
    [InlineData("CREATE TABLE #probe(Id int);")]
    [InlineData("SELECT 1 AS Id INTO #probe;")]
    [InlineData("SELECT 1 AS Id INTO dbo.Probe;")]
    [InlineData("BEGIN TRANSACTION;")]
    [InlineData("COMMIT;")]
    [InlineData("ROLLBACK;")]
    [InlineData("SAVE TRANSACTION probe;")]
    [InlineData("DECLARE @x int;")]
    [InlineData("SELECT @x=Id FROM dbo.T;")]
    [InlineData("IF 1=0 DELETE FROM dbo.T;")]
    [InlineData("BEGIN TRY DELETE FROM dbo.T; END TRY BEGIN CATCH SELECT 1; END CATCH;")]
    [InlineData("SELECT 1;\nGO\nDELETE FROM dbo.T;")]
    public void Suite_MutatingObserverIsRejectedEvenWhenSupportedAsSetup(string observer)
    {
        Action setup = () => SqlSemanticSandboxPolicy.ParseBatches(observer);
        setup.Should().NotThrow();
        Action validate = () => SqlSemanticSandboxPolicy.Validate(new([new("case", "", observer)]));
        validate.Should().Throw<InvalidDataException>().WithMessage("*ObserverReadOnly*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("SELECT @@ROWCOUNT AS LastAffected, @@ERROR AS LastError;")]
    [InlineData("SELECT ROWCOUNT_BIG() AS LastAffected;")]
    [InlineData("SELECT N'DELETE; SELECT INTO; @@ROWCOUNT' AS Text; -- UPDATE dbo.T\n")]
    [InlineData("SELECT Id FROM dbo.T ORDER BY Id;\nGO\nSELECT COUNT(*) AS Total FROM dbo.T;")]
    [InlineData("WITH selected AS (SELECT Id FROM dbo.T) SELECT * FROM selected;")]
    [InlineData("SELECT (SELECT COUNT(*) FROM dbo.T) AS Total;")]
    public void Suite_ReadOnlyObserverAndSessionValuesRemainSupported(string observer)
    {
        Action validate = () => SqlSemanticSandboxPolicy.Validate(new([new("case", "", observer)]));
        validate.Should().NotThrow();
    }

    [Fact]
    public async Task Runner_InvalidObserverFailsBeforeOpeningDatabase()
    {
        var suite = new SqlSemanticSuite([new("case", "", "DELETE FROM dbo.T;")], "SqlXmlAnalyzer_NonexistentObserverTest");
        var result = await new LocalDbSqlSemanticRunner().ValidateAsync("SELECT 1;", "SELECT 1;", suite);
        result.Status.Should().Be(SemanticValidationStatus.Failed);
        result.CanPrepareApply.Should().BeFalse();
        result.ServerVersion.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().Contain("ObserverReadOnly");
        result.Diagnostics.Should().BeEmpty();
    }
}
