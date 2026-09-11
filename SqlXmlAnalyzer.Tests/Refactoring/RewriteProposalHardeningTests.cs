using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Refactoring;

namespace SqlXmlAnalyzer.Tests.Refactoring;

public sealed class RewriteProposalHardeningTests
{
    [Theory]
    [InlineData("SELECT 1 AS Id;", "SELECT 1 AS Id INTO #stage;")]
    [InlineData("SELECT 1 AS Id INTO #stage;", "SELECT 1 AS Id;")]
    [InlineData("SELECT 1 AS Id INTO #stage;", "SELECT 1 AS Id INTO #other;")]
    [InlineData("SELECT 1 AS Id INTO #stage;", "SELECT 1 AS Id INTO ##stage;")]
    [InlineData("SELECT 1 AS Id INTO #stage;", "SELECT 1 AS Id INTO #Stage;")]
    [InlineData("SELECT 1 AS Id INTO [#stage.name];", "SELECT 1 AS Id INTO [#other.name];")]
    [InlineData("SELECT 1 AS Id INTO #stage;", "SELECT 1 AS Id INTO tempdb..#stage;")]
    [InlineData("SELECT 1 AS Id INTO #stage;", "SELECT 1 AS Id INTO dbo.stage;")]
    [InlineData("SELECT 1 AS Id;", "SELECT 1 AS Id INTO dbo.stage;")]
    [InlineData(";WITH c AS (SELECT 1 AS Id) SELECT Id FROM c;", ";WITH c AS (SELECT 1 AS Id) SELECT Id INTO #stage FROM c;")]
    [InlineData("CREATE TABLE #stage(Id int);", "SELECT 1 AS Id INTO #stage;")]
    [InlineData("SELECT 1 AS Id INTO #stage;\nGO\nSELECT 2;", "SELECT 2;\nGO\nSELECT 1 AS Id INTO #stage;")]
    [InlineData("CREATE TABLE #stage(Id int);", "CREATE TABLE #Stage(Id int);")]
    [InlineData("DROP TABLE #stage;", "DROP TABLE IF EXISTS #stage;")]
    [InlineData("DROP TABLE #stage;", "DROP TABLE tempdb..#stage;")]
    [InlineData("DECLARE @stage TABLE(Id int);", "DECLARE @Stage TABLE(Id int);")]
    [InlineData("IF 1=0 SELECT 1 AS Id INTO #stage;", "SELECT 1 AS Id INTO #stage;")]
    [InlineData("IF 1=0 SELECT 1 AS Id INTO #stage; ELSE SELECT 2;", "IF 1=0 SELECT 2; ELSE SELECT 1 AS Id INTO #stage;")]
    [InlineData("IF 1=0 CREATE TABLE #stage(Id int);", "IF 1=1 CREATE TABLE #stage(Id int);")]
    [InlineData("WHILE 1=0 SELECT 1 AS Id INTO #stage;", "SELECT 1 AS Id INTO #stage;")]
    [InlineData("BEGIN TRY DROP TABLE #stage; END TRY BEGIN CATCH SELECT 1; END CATCH;", "BEGIN TRY SELECT 1; END TRY BEGIN CATCH DROP TABLE #stage; END CATCH;")]
    public void Validate_ObjectLifecycleChange_IsRejected(string source, string candidate)
    {
        var result = RewriteProposalService.Validate(source, candidate);
        result.SyntaxValid.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.StartsWith("TemporaryObjectOwnership:"));
        result.CanApply.Should().BeFalse();
    }

    [Theory]
    [InlineData("SELECT 1 AS Id INTO #stage;", "-- preserved target\nselect 2 as Id into [#stage];")]
    [InlineData("SELECT 1 AS Id INTO tempdb..#stage;", "select 2 as Id into [tempdb]..[#stage];")]
    [InlineData("IF 1=0 SELECT 1 AS Id INTO #stage;", "if 1 = 0 select 2 as Id into #stage;")]
    [InlineData("IF 1=0 SELECT 1;", "IF 1=1 SELECT 2;")]
    [InlineData("SELECT '#stage' AS Id; -- INTO #stage", "SELECT '#other' AS Id; -- INTO #other")]
    public void Validate_UnchangedObjectStructure_AllowsUnprovenCandidate(string source, string candidate)
    {
        var result = RewriteProposalService.Validate(source, candidate);
        result.IsValid.Should().BeTrue();
        result.Equivalence.Should().Be(RewriteEquivalence.Unproven);
        result.CanApply.Should().BeFalse();
    }

    [Fact]
    public void Review_PreviouslyValidStepCannotHideAddedSelectInto()
    {
        const string source = "SELECT 1 AS Id;";
        const string candidate = "SELECT 2 AS Id INTO #stage;";
        var reporter = new UnexpectedReporter();
        var service = new RewriteProposalService(reporter);
        var proposal = service.Propose(source, source, "SELECT 2 AS Id;", "R1", "1", "test", [], new(source));
        var changed = proposal with
        {
            Diff = new(0, source, candidate), CandidateHash = SqlTextHash.Compute(candidate)
        };
        Action review = () => service.Review(source, [changed], [changed.Id]);
        review.Should().Throw<InvalidDataException>().WithMessage("CombinedValidationFailed: *TemporaryObjectOwnership*");
        reporter.Calls.Should().Be(0, "a rejected object change is an expected validation error");
    }

    [Fact]
    public void Engine_ObjectChangingRule_DiscardsCandidateAndKeepsSource()
    {
        const string source = "SELECT 1 AS Id;";
        var engine = new SqlRefactoringEngine([new ObjectChangingRule()], new DefaultRuleFilter(),
            NullLogger<SqlRefactoringEngine>.Instance);
        var result = engine.Run(source, new AnalysisReport([]), new(), false);
        result.IsSuccess.Should().BeFalse();
        result.OutputSql.Should().Be(source);
        result.Review.Should().BeNull();
        result.Context.Changed.Should().BeFalse();
        result.SourceWritten.Should().BeFalse();
        result.Diagnostic.Should().BeNull();
    }

    private sealed class ObjectChangingRule : ISqlRefactorRule
    {
        public string RuleId => "TEST_OBJECT_CHANGE";
        public string Name => "Test object creation";
        public string Description => Name;
        public int Priority => 1;
        public bool CanApply(TSqlFragment fragment, RefactorContext context) => true;
        public RuleResult Apply(TSqlFragment fragment, RefactorContext context) =>
            new(new TSql160Parser(true).Parse(new StringReader("SELECT 1 AS Id INTO #stage;"), out _), true, Name);
    }

    private sealed class UnexpectedReporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Calls++;
            return new(null, null, "Unexpected reporter called");
        }
    }
}
