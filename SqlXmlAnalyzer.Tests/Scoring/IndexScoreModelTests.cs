using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;
using SqlXmlAnalyzer.Core.Scoring;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Simulation;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests.Scoring;

public sealed class IndexScoreModelTests
{
    private static readonly XNamespace Ns = CostImpactModelTests.Ns;

    [Theory]
    [InlineData("[K]=1")]
    [InlineData("[K] = 1")]
    [InlineData("1=[K]")]
    [InlineData("[db].[dbo].[T].[K]=[@p]")]
    public void ScalarTextFallback_WhitespaceDoesNotChangeEqualityPoints(string predicate)
    {
        var (doc, candidate) = Fixture(predicate);
        var score = IndexScoringCalculator.Evaluate(candidate, doc, Ns);
        score.EqualityPoints.Should().Be(30);
        score.Score.Should().Be(70);
        score.EvidenceSource.Should().Be("SCALAR_TEXT_SYNTAX");
    }

    [Theory]
    [InlineData("[OtherK]=1")]
    [InlineData("[K2]=1")]
    [InlineData("[K]=1 OR [V]=2")]
    [InlineData("NOT ([K]=1)")]
    [InlineData("ABS([K])=1")]
    [InlineData("[other].[dbo].[T].[K]=1")]
    [InlineData("[K]=[V]")]
    [InlineData("1=1; SELECT 1 WHERE [K]=1")]
    public void ScalarTextFallback_DoesNotGuessFromSubstringsDisjunctionsOrWrongTargets(string predicate)
    {
        var (doc, candidate) = Fixture(predicate);
        IndexScoringCalculator.Evaluate(candidate, doc, Ns).EqualityPoints.Should().Be(0);
    }

    [Fact]
    public void CapturedStructuredPredicate_TakesPrecedenceOverConflictingScalarText()
    {
        var doc = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp15_review_actual.sqlplan"));
        var candidate = CostImpactModelTests.Candidates(doc).Single();
        candidate.KeyColumns.Add(new() { Name = "[R]", Usage = "INEQUALITY" });
        foreach (var scalar in doc.Descendants(Ns + "ScalarOperator").Where(s => s.Attribute("ScalarString") != null))
            scalar.SetAttributeValue("ScalarString", "[OtherK]=999");
        candidate = CostImpactModelTests.Candidates(doc).Single();
        candidate.KeyColumns.Add(new() { Name = "[R]", Usage = "INEQUALITY" });
        var result = IndexScoringCalculator.Evaluate(candidate, doc, Ns);
        result.EqualityPoints.Should().Be(30);
        result.SortPoints.Should().Be(15);
        result.EvidenceSource.Should().Be("CAPTURED_PREDICATES");
    }

    [Fact]
    public void CapturedStructuredDisjunction_DoesNotBecomeAConjunctiveIndexPrefix()
    {
        var doc = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp15_review_actual.sqlplan"));
        var compare = doc.Descendants(Ns + "Compare").First();
        var copy = new XElement(compare);
        compare.ReplaceWith(new XElement(Ns + "Logical", new XAttribute("Operation", "OR"), new XElement(Ns + "ScalarOperator", copy)));
        var candidate = CostImpactModelTests.Candidates(doc).Single();
        IndexScoringCalculator.Evaluate(candidate, doc, Ns).EqualityPoints.Should().Be(0);
    }

    [Fact]
    public void Scoring_StatementReorderAndSequentialCandidateEvaluationAreIndependent()
    {
        (string Input, int Score)[] Read(bool reverse)
        {
            var doc = CostImpactModelTests.Plan(reverse);
            // Give the two statements distinct evidence and match by the captured input marker,
            // rather than comparing an unlabelled bag of equal scores.
            doc.Descendants(Ns + "StmtSimple").Single(s => (string?)s.Attribute("StatementSubTreeCost") == "100")
                .Descendants(Ns + "ScalarOperator").First().SetAttributeValue("ScalarString", "[V]=1");
            var candidates = CostImpactModelTests.Candidates(doc);
            return (reverse ? candidates.Reverse() : candidates).Select(c =>
            {
                var op = IndexTargetResolver.FindOperators(c, doc, Ns).Single();
                return (Input: (string)op.Ancestors(Ns + "StmtSimple").First().Attribute("StatementSubTreeCost")!,
                    Score: IndexScoringCalculator.Evaluate(c, doc, Ns).Score);
            }).OrderByDescending(item => item.Score).ToArray();
        }
        Read(false).Should().Equal(Read(true)).And.Equal(("10", 70), ("100", 40));
    }

    [Fact]
    public void ScoreResult_ExplainsWeightsSourceScopeAndMissingCoverage()
    {
        var candidate = new MissingIndexSuggestion { KeyColumns = [new() { Name = "[K]", Usage = "EQUALITY" }] };
        var result = IndexScoringCalculator.Evaluate(candidate, null, null);
        result.Score.Should().Be(30);
        result.KnownOutputCoverage.Should().BeNull();
        result.CoveragePoints.Should().Be(0);
        result.ModelVersion.Should().Be(IndexScoreResult.Version);
        result.Weights.Should().Contain("30").And.Contain("40").And.Contain("0–100");
        result.Limitations.Should().Contain("非 SQL Server");
        JsonSerializer.Serialize(result).Should().Contain("DEFINITION_ROLES_ONLY").And.Contain("ModelVersion");
    }

    [Fact]
    public void Sandbox_ExposesSeparateImpactScoreAndInputAssumptionSources()
    {
        var (doc, candidate) = Fixture("[K]=1");
        candidate.Source = IndexSuggestionSource.CapturedMissingIndex;
        candidate.CapturedImpact = 82.5;
        var vm = new IndexSandboxViewModel(candidate, doc);
        vm.ImpactSummary.Should().Contain("82.5%").And.Contain("非实测");
        vm.ScoreBreakdown.Should().Contain("70/100");
        vm.ModelSummary.Should().Contain(IndexScoreResult.Version);
        vm.InputAssumptionsNotice.Should().Contain("最大值").And.Contain("假设");
        vm.TotalRows = 900;
        vm.InputAssumptionsNotice.Should().Contain("用户编辑");
        vm.CostReductionSummary.Should().Contain("N/A").And.NotContain("%");
        vm.CostEvidenceSummary.Should().Contain("不相加");
    }

    [Fact]
    public void SyntaxCandidate_CannotInventSqlServerImpact()
    {
        var candidate = MissingIndexSuggester.SuggestIndexes("SELECT K FROM dbo.T WHERE K=1").Single();
        candidate.Source.Should().Be(IndexSuggestionSource.SqlSyntax);
        candidate.CapturedImpact.Should().BeNull();
        candidate.Impact.Should().Be(0);
        new IndexSandboxViewModel(candidate).ImpactSummary.Should().Contain("N/A");
        new MissingIndexDeploymentScriptService().BuildDeploymentBundle(candidate).Should().Contain("SQL Server Impact: N/A");
        candidate.CapturedImpact = 80; // A value without captured provenance must not become SQL Server evidence.
        new MissingIndexDeploymentScriptService().BuildDeploymentBundle(candidate).Should().Contain("SQL Server Impact: N/A");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NaN")]
    [InlineData("-1")]
    [InlineData("101")]
    public void MissingOrInvalidCapturedImpact_RemainsUnknown(string? value)
    {
        var doc = IndexTargetIdentityTests.Fixture();
        doc.Descendants(Ns + "MissingIndexGroup").First().SetAttributeValue("Impact", value);
        var candidate = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, Ns).First();
        candidate.CapturedImpact.Should().BeNull();
        new IndexSandboxViewModel(candidate, doc).ImpactSummary.Should().Contain("N/A");
    }

    [Fact]
    public void Sandbox_ZeroEstimatesArePreservedAndMissingEstimatesDeclareDefaults()
    {
        var (doc, _) = Fixture("[K]=1");
        var op = doc.Descendants(Ns + "RelOp").First(e => e.Attribute("TableCardinality") != null);
        op.SetAttributeValue("EstimateRows", 0);
        op.SetAttributeValue("AvgRowSize", null);
        var candidate = CostImpactModelTests.Candidates(doc)[0];
        var vm = new IndexSandboxViewModel(candidate, doc);
        vm.ReturnedRows.Should().Be(0);
        vm.AvgRowSize.Should().Be(200);
        vm.InputAssumptionsNotice.Should().Contain("工具默认 200");
    }

    private static (XDocument, MissingIndexSuggestion) Fixture(string predicate)
    {
        var doc = CostImpactModelTests.Plan();
        doc.Descendants(Ns + "ScalarOperator").First().SetAttributeValue("ScalarString", predicate);
        doc.Descendants(Ns + "QueryPlan").First().Add(new XElement(Ns + "ParameterList",
            new XElement(Ns + "ColumnReference", new XAttribute("Column", "@p"), new XAttribute("ParameterDataType", "int"))));
        return (doc, CostImpactModelTests.Candidates(doc)[0]);
    }
}
