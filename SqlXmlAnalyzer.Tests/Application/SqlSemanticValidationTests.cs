using System.Collections.Immutable;
using System.IO;
using System.Globalization;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class SqlSemanticValidationTests
{
    [Theory]
    [InlineData("EXEC('SELECT 1');")]
    [InlineData("REVERT;")]
    [InlineData("USE master;")]
    [InlineData("DROP DATABASE other;")]
    [InlineData("ALTER AUTHORIZATION ON DATABASE::current TO sa;")]
    [InlineData("SELECT * FROM master.sys.databases;")]
    [InlineData("SELECT 1 AS Id INTO ##shared;")]
    [InlineData("CREATE PROCEDURE dbo.P AS SELECT 1;")]
    [InlineData("SET XACT_ABORT ON;")]
    [InlineData("SELECT * FROM OPENROWSET(BULK 'C:\\private.txt', SINGLE_CLOB) AS contents;")]
    [InlineData("SELECT * FROM OPENQUERY(remote, 'SELECT 1');")]
    [InlineData("SELECT NEXT VALUE FOR dbo.seq;")]
    public void Sandbox_RejectsUnsupportedAndEscapingStatements(string sql)
    {
        Action parse = () => SqlSemanticSandboxPolicy.ParseBatches(sql);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("SELECT N'GO' AS Value; -- GO\nGO\nSELECT 2;", 2)]
    [InlineData("DECLARE @t TABLE(Id int); BEGIN TRANSACTION; INSERT @t VALUES(1); ROLLBACK; SELECT * FROM @t;", 1)]
    [InlineData("BEGIN TRY THROW 50001, 'test', 1; END TRY BEGIN CATCH SELECT ERROR_NUMBER(); END CATCH;", 1)]
    public void Sandbox_ParsesSupportedBatchesWithoutSplittingStrings(string sql, int count) =>
        SqlSemanticSandboxPolicy.ParseBatches(sql).Should().HaveCount(count);

    [Theory]
    [InlineData("server", 150)]
    [InlineData("SqlXmlAnalyzer_x;bad", 150)]
    [InlineData("SqlXmlAnalyzer_Test", 160)]
    public void Suite_RejectsUnsupportedServerAndVersion(string instance, int compatibility)
    {
        Action validate = () => SqlSemanticSandboxPolicy.Validate(new([new("case", "")], instance, compatibility));
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Suite_EmptyDuplicateOrInvalidScenariosCannotPass()
    {
        Action empty = () => SqlSemanticSandboxPolicy.Validate(new([]));
        empty.Should().Throw<InvalidDataException>();
        Action duplicate = () => SqlSemanticSandboxPolicy.Validate(new([new("same", ""), new("same", "")]));
        duplicate.Should().Throw<InvalidDataException>();
        Action setup = () => SqlSemanticSandboxPolicy.Validate(new([new("case", "EXEC('SELECT 1');")]));
        setup.Should().Throw<InvalidDataException>();
    }

    private static SqlExecutionObservation Rows(params string[] rows) => new([new(["int"], rows.ToImmutableArray())], [], -1);

    [Fact]
    public void Comparison_PreservesMultiplicityTypesNullAndOptionalOrder()
    {
        SqlSemanticComparison.Equal(Rows("1", "1", "2"), Rows("2", "1", "1"), false).Should().BeTrue();
        SqlSemanticComparison.Equal(Rows("1", "1", "2"), Rows("2", "1"), false).Should().BeFalse();
        SqlSemanticComparison.Equal(Rows("1", "2"), Rows("2", "1"), true).Should().BeFalse();
        SqlSemanticComparison.Equal(Rows("1"), Rows("1") with { Results = [new(["nvarchar"], ["1"])] }, false).Should().BeFalse();
        SqlSemanticComparison.EncodeRow([null]).Should().NotBe(SqlSemanticComparison.EncodeRow([""]));
        SqlSemanticComparison.EncodeRow([1]).Should().NotBe(SqlSemanticComparison.EncodeRow(["1"]));
        SqlSemanticComparison.EncodeRow(["a,b", "c"]).Should().NotBe(SqlSemanticComparison.EncodeRow(["a", "b,c"]));
    }

    [Fact]
    public void Comparison_IncludesErrorsAffectedRowsTransactionsAndObjectState()
    {
        var execution = Rows("1");
        var observation = new SqlSemanticObservation(execution, execution, execution, 0, 0, 1);
        SqlSemanticComparison.Equal(observation, observation with { TransactionCount = 1 }, false).Should().BeFalse();
        SqlSemanticComparison.Equal(observation, observation with { TransactionState = -1 }, false).Should().BeFalse();
        SqlSemanticComparison.Equal(observation, observation with { SessionOptions = 2 }, false).Should().BeFalse();
        SqlSemanticComparison.Equal(observation, observation with { ObjectState = Rows("2") }, false).Should().BeFalse();
        SqlSemanticComparison.Equal(execution, execution with { RecordsAffected = 2 }, false).Should().BeFalse();
        SqlSemanticComparison.Equal(execution, execution with { ErrorNumbers = [8115] }, false).Should().BeFalse();
    }

    [Fact]
    public void Encoding_IsIndependentOfCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string first = SqlSemanticComparison.EncodeRow([1.25m, DateTime.UnixEpoch, new byte[] { 0, 1, 255 }]);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            SqlSemanticComparison.EncodeRow([1.25m, DateTime.UnixEpoch, new byte[] { 0, 1, 255 }]).Should().Be(first);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void SuiteFingerprint_ChangesWithFixtureAndEnvironment()
    {
        var suite = new SqlSemanticSuite([new("case", "SELECT 1;")]);
        suite.Fingerprint.Should().Be(suite.Fingerprint);
        (suite with { Scenarios = [new("case", "SELECT 2;")] }).Fingerprint.Should().NotBe(suite.Fingerprint);
        (suite with { CompatibilityLevel = 150 }).Fingerprint.Should().NotBe(suite.Fingerprint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task ValueReader_PreservesCompleteBoundedValues(int length)
    {
        string value = new('漢', length);
        (await SqlSemanticValueReader.ReadTextAsync(new StringReader(value))).Should().Be(value);
        byte[] bytes = Enumerable.Repeat((byte)255, length).ToArray();
        (await SqlSemanticValueReader.ReadBytesAsync(new MemoryStream(bytes))).Should().Equal(bytes);
    }

    [Fact]
    public async Task ValueReader_OversizedAndCanceledValuesCannotBecomeTruncatedEvidence()
    {
        Func<Task> text = () => SqlSemanticValueReader.ReadTextAsync(new StringReader(new string('x', 65537)));
        await text.Should().ThrowAsync<InvalidDataException>();
        Func<Task> bytes = () => SqlSemanticValueReader.ReadBytesAsync(new MemoryStream(new byte[65537]));
        await bytes.Should().ThrowAsync<InvalidDataException>();
        Func<Task> canceled = () => SqlSemanticValueReader.ReadTextAsync(new StringReader("test"), new CancellationToken(true));
        await canceled.Should().ThrowAsync<OperationCanceledException>();
    }
}
