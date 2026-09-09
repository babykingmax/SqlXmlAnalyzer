using System.Collections.Immutable;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Refactoring.Rules;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests.Refactoring;

public sealed class RewriteProposalTests
{
    internal const string Sql = "-- 保留原注释\r\nSELECT * FROM PrivateUsers WHERE Age + 10 > 50 AND LTRIM(Name) = N'admin';\r\n";
    internal static RefactorResult Run(string sql = Sql, RefactorOptions? options = null) =>
        new SqlRefactoringEngine([new ConstantFoldingRefactorRule(), new TrimRefactorRule()], new DefaultRuleFilter(),
            NullLogger<SqlRefactoringEngine>.Instance).Run(sql, new AnalysisReport([]), options ?? new(), false);

    [Fact]
    public void Run_Default_ReturnsAuditableUnselectedProposalAndExactOriginalPreview()
    {
        var result = Run();
        result.IsSuccess.Should().BeTrue();
        var review = result.Review!;
        review.PreviewSql.Should().Be(Sql);
        review.SourceHash.Should().Be(SqlTextHash.Compute(Sql));
        review.CanApply.Should().BeFalse();
        var proposal = review.Proposals.Should().ContainSingle().Which;
        proposal.IsSelected.Should().BeFalse();
        proposal.RuleVersion.Should().NotBeNullOrWhiteSpace();
        proposal.SourceHash.Should().Be(review.SourceHash);
        proposal.Preconditions.Should().NotBeEmpty();
        proposal.Risks.Should().NotBeEmpty();
        proposal.Evidence.Should().NotBeEmpty();
        proposal.Validation.SyntaxValid.Should().BeTrue();
        proposal.Validation.Equivalence.Should().Be(RewriteEquivalence.Unproven);
        proposal.Validation.CanApply.Should().BeFalse();
        proposal.Validation.CheckedProperties.Should().NotBeEmpty();
        proposal.Validation.UnprovenProperties.Should().NotBeEmpty();
        review.Warnings.Should().Contain(w => w.Contains("REF_RULE_103_TRIM"));
        new RewriteProposalService().Review(Sql, review.Proposals, [proposal.Id]).PreviewSql.Should().Be(result.OutputSql);
    }

    [Fact]
    public void Review_SelectThenDeselect_RestoresOriginalWhitespaceAndComments()
    {
        var proposals = Run().Review!.Proposals;
        var service = new RewriteProposalService();
        var selected = service.Review(Sql, proposals, proposals.Select(p => p.Id));
        selected.PreviewSql.Should().Contain("Age > 40");
        selected.CanApply.Should().BeFalse();
        service.Review(Sql, selected.Proposals, []).PreviewSql.Should().Be(Sql);
    }

    [Fact]
    public void Run_SameSourceAndVersions_ProducesStableIdsAcrossRuns()
    {
        Run().Review!.Proposals[0].Id.Should().Be(Run().Review!.Proposals[0].Id);
        Run(Sql + " ").Review!.Proposals[0].Id.Should().NotBe(Run().Review!.Proposals[0].Id);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void Run_InvalidSelection_FailsWithoutPartialCandidate(string id)
    {
        var result = Run(options: new(SelectedProposalIds: [id]));
        result.IsSuccess.Should().BeFalse();
        result.OutputSql.Should().Be(Sql);
        result.Review.Should().BeNull();
        result.Context.Changed.Should().BeFalse();
        result.Diagnostic.Should().BeNull();
    }

    [Fact]
    public void Review_StaleSourceOrDuplicateIds_RejectsEvenWhenNothingIsSelected()
    {
        var proposal = Run().Review!.Proposals[0];
        var service = new RewriteProposalService();
        Action stale = () => service.Review(Sql + " ", [proposal]);
        stale.Should().Throw<InvalidDataException>().WithMessage("SourceHashMismatch*");
        Action duplicate = () => service.Review(Sql, [proposal, proposal]);
        duplicate.Should().Throw<InvalidDataException>().WithMessage("DuplicateProposal*");
    }

    internal static (RewriteProposal First, RewriteProposal Second) Chain()
    {
        var service = new RewriteProposalService();
        var context = new RefactorContext("SELECT 1;");
        var first = service.Propose("SELECT 1;", "SELECT 1;", "SELECT 2;", "R1", "1", "first", [], context);
        var second = service.Propose("SELECT 1;", "SELECT 2;", "SELECT 3;", "R2", "2", "second", [first.Id], context);
        return (first, second);
    }

    [Fact]
    public void Review_MultipleSteps_RequiresDependenciesAndRevalidatesComposition()
    {
        var (first, second) = Chain();
        var service = new RewriteProposalService();
        Action missing = () => service.Review("SELECT 1;", [first, second], [second.Id]);
        missing.Should().Throw<InvalidDataException>().WithMessage("ProposalDependency*");
        var review = service.Review("SELECT 1;", [first, second], [first.Id, second.Id]);
        review.PreviewSql.Should().Be("SELECT 3;");
        review.Validation.SyntaxValid.Should().BeTrue();
        review.Validation.CanApply.Should().BeFalse();

        // A previously marked valid step must not bypass a fresh parse of the combined text.
        string invalidSql = "SELECT FROM;";
        var poisoned = second with
        {
            Diff = new(0, "SELECT 2;", invalidSql), CandidateHash = SqlTextHash.Compute(invalidSql)
        };
        Action invalid = () => service.Review("SELECT 1;", [first, poisoned], [first.Id, second.Id]);
        invalid.Should().Throw<InvalidDataException>().WithMessage("CombinedValidationFailed*");
    }

    [Theory]
    [InlineData("base")]
    [InlineData("candidate")]
    [InlineData("offset")]
    [InlineData("text")]
    public void Review_TamperedStep_RejectsWithoutPreview(string field)
    {
        var (first, _) = Chain();
        first = field switch
        {
            "base" => first with { BaseSqlHash = "stale" },
            "candidate" => first with { CandidateHash = "stale" },
            "offset" => first with { Diff = first.Diff with { StartOffset = int.MaxValue } },
            _ => first with { Diff = first.Diff with { OriginalText = "wrong" } }
        };
        Action act = () => new RewriteProposalService().Review("SELECT 1;", [first], [first.Id]);
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("CREATE TABLE #stage(Id int); CREATE TABLE #stage(Id int);")]
    [InlineData("DROP TABLE #stage;")]
    [InlineData("DECLARE @stage TABLE(Id int);")]
    [InlineData("CREATE TABLE #stage(Id int);\nGO\nSELECT 1;")]
    public void Validate_TemporaryObjectOwnershipChange_IsRejected(string candidate)
    {
        var validation = RewriteProposalService.Validate("CREATE TABLE #stage(Id int);", candidate);
        validation.SyntaxValid.Should().BeTrue();
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("TemporaryObjectOwnership"));
    }

    [Fact]
    public void Scope_MultipleDeclarationsAndExistingName_ReservesDistinctNamesWithoutCreatingOrDropping()
    {
        const string sql = "CREATE TABLE #stage(Id int); DECLARE @stage TABLE(Id int); DECLARE @other TABLE(Id int);\nGO\nDECLARE @stage TABLE(Id int);";
        var ast = new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
        errors.Should().BeEmpty();
        var scope = SqlRewriteScope.Analyze(sql);
        scope.TableVariables.Should().HaveCount(3);
        scope.TemporaryNames.Should().Contain("#stage");
        scope.Reservations.Should().HaveCount(3).And.OnlyHaveUniqueItems(r => r.TemporaryName);
        scope.Reservations.Should().OnlyContain(r => r.TemporaryName != "#stage" && r.OwnerSourceHash == SqlTextHash.Compute(sql));
        var context = new RefactorContext(sql);
        ast.Accept(new TableVariableVisitor(context));
        context.Changed.Should().BeFalse();
        context.SafetySkips.Should().NotBeEmpty();
        new TableVariableRefactorRule().Apply(ast, context).IsApplied.Should().BeFalse();
    }

    [Theory]
    [InlineData("IF 1=1 BEGIN DECLARE @a TABLE(Id int); END;")]
    [InlineData("CREATE PROCEDURE P AS BEGIN DECLARE @a TABLE(Id int); END;")]
    [InlineData("CREATE FUNCTION F() RETURNS int AS BEGIN DECLARE @a TABLE(Id int); RETURN 1; END;")]
    [InlineData("DECLARE @a TABLE(Id int); EXEC(N'SELECT 1');")]
    [InlineData("DECLARE @a TABLE(Id int); DECLARE @a TABLE(Id int);")]
    public void Scope_UncertainOrDuplicateDeclarations_SkipsReservationWithReason(string sql)
    {
        var ast = new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
        errors.Should().BeEmpty();
        var scope = SqlRewriteScope.Analyze(sql);
        scope.Reservations.Should().BeEmpty();
        scope.Limitations.Should().NotBeEmpty();
        var context = new RefactorContext(sql);
        new TableVariableRefactorRule().Apply(ast, context);
        context.SafetySkips.Should().Contain(s => s.ReasonCode == "UnsupportedTableVariableScope");
    }

    [Fact]
    public void ViewModel_SelectingDependentSelectsPrefixAndCancelingParentRestoresOriginal()
    {
        var (first, second) = Chain();
        var review = new RewriteProposalService().Review("SELECT 1;", [first, second]);
        var model = new RewriteReviewViewModel("SELECT 1;", review);
        model.Items.Should().OnlyContain(i => !i.IsSelected);
        model.Items[1].IsSelected = true;
        model.Items.Should().OnlyContain(i => i.IsSelected);
        model.PreviewSql.Should().Be("SELECT 3;");
        model.Items[0].IsSelected = false;
        model.Items.Should().OnlyContain(i => !i.IsSelected);
        model.PreviewSql.Should().Be("SELECT 1;");
        model.CanApply.Should().BeFalse();
        model.Error.Should().BeEmpty();
    }

    [Fact]
    public void Scope_DeclarationOffset_IsBoundToExactSourceWhitespaceAndComments()
    {
        const string sql = "-- 注释\r\n\r\n  DECLARE   @stage TABLE ( Id int );";
        var scope = SqlRewriteScope.Analyze(sql);
        scope.TableVariables.Should().ContainSingle().Which.Offset.Should().Be(sql.IndexOf("DECLARE", StringComparison.Ordinal));
        scope.Reservations.Should().ContainSingle().Which.OwnerSourceHash.Should().Be(SqlTextHash.Compute(sql));
    }

    [Fact]
    public void GuiPresentation_UsesSameReviewAndPreservesAllWarningsOutsideSql()
    {
        var result = Run();
        var presentation = RefactorPresentation.FromResult(new OrchestratorResult(result, true, null, null, []), Sql);
        presentation.Review.Should().BeSameAs(result.Review);
        presentation.Sql.Should().Be(Sql);
        presentation.Notices.Should().Contain(result.Review!.SourceHash).And.Contain("等价性").And.Contain("REF_RULE_103_TRIM");
        presentation.Notices.Should().Contain("SQL diff");
    }

    [Fact]
    public void QuickFix_ReturnsSameProposalContractWithoutChangingOriginalPreview()
    {
        const string sql = "SELECT c.Id, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId=c.Id) AS N FROM Customers c;";
        var sub = ScalarSubqueryToJoinRule.GetRewriteableSubqueries(sql).Single();
        var result = new Core.Services.SqlQuickFixService().TryRewriteSelectedSubquery(sql, sub.StartOffset, sub.FragmentLength);
        result.IsAvailable.Should().BeTrue();
        result.Review!.PreviewSql.Should().Be(sql);
        result.Review.Proposals.Should().ContainSingle(p => !p.IsSelected);
        result.Review.CanApply.Should().BeFalse();
    }
}
