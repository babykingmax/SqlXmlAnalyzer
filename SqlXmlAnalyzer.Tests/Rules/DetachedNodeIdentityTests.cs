using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Rules;
using static SqlXmlAnalyzer.Tests.Rules.CardinalityResidualRuleTests;
using NativeRule = SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests.NativeRule;

namespace SqlXmlAnalyzer.Tests.Rules;

public sealed class DetachedNodeIdentityTests
{
    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(30, false)]
    [InlineData(30, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(7, true)]
    [InlineData(34, false)]
    [InlineData(34, true)]
    public void AnalyzeNode_StandaloneOperatorRetainsNodeIdForEveryImp14Result(int ruleNumber, bool detached)
    {
        IPlanAnalyzerRule rule = ruleNumber switch
        {
            4 => new RowEstimateMismatchRule(), 30 => new CardinalityErrorRule(),
            6 or 7 => new ResidualPredicateRule(), 34 => new ResidualPredOpRule(),
            _ => throw new ArgumentOutOfRangeException(nameof(ruleNumber))
        };
        XElement op = Sample(ruleNumber == 7, detached);
        var direct = rule.Analyze(op, Ns)!;
        var result = Engine(rule).AnalyzeNode(op, Ns).Should().ContainSingle().Subject;
        result.RuleId.Should().Be(direct.RuleId);
        result.NodeId.Should().Be("42").And.Be(direct.NodeId);
        result.Location!.Operator.Should().BeNull("a standalone operator has no batch/statement identity");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MergedDiagnostics_PreserveNodeIdAcrossEntryOrderAndJson(bool detached, bool reverse)
    {
        IPlanAnalyzerRule[] rules = [new RowEstimateMismatchRule(), new CardinalityErrorRule(),
            new ResidualPredicateRule(), new ResidualPredOpRule()];
        if (reverse) Array.Reverse(rules);
        var op = Sample(detached: detached);
        var engine = Engine(rules);
        var report = detached ? engine.AnalyzeNodeDetailed(op, Ns) : engine.AnalyzePlanDetailed(op.Document!, Ns);
        report.Diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.NodeId == "42" && d.Origins.Count == 2);
        report.Runs.Should().HaveCount(4).And.OnlyContain(r => r.NodeId == "42");
        report.ToLegacyResults().Should().OnlyContain(r => r.NodeId == "42");
        DiagnosticTextFormatter.Format(report).Should().Contain("局部 NodeId：42");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report));
        foreach (string collection in new[] { "Diagnostics", "Runs" })
            json.RootElement.GetProperty(collection).EnumerateArray().Should()
                .OnlyContain(e => e.GetProperty("NodeId").GetString() == "42"
                    && e.GetProperty("Location").GetProperty("Operator").ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    public void NodeId_PreservesMissingEmptyAndZeroWithoutInventingIdentity(string? nodeId)
    {
        var op = Sample(detached: true);
        op.SetAttributeValue("NodeId", nodeId);
        var report = Engine(new RowEstimateMismatchRule()).AnalyzeNodeDetailed(op, Ns);
        report.Diagnostics.Single().NodeId.Should().Be(nodeId);
        report.Runs.Single().NodeId.Should().Be(nodeId);
        report.ToLegacyResults().Single().NodeId.Should().Be(nodeId ?? "");
        report.Diagnostics.Single().Location.Operator.Should().BeNull();
    }

    [Fact]
    public void PublishedIdentity_IsSnapshotAndFreshAnalysisObservesSourceChanges()
    {
        var op = Sample(detached: true);
        var engine = Engine(new RowEstimateMismatchRule());
        var before = engine.AnalyzeNodeDetailed(op, Ns);
        op.SetAttributeValue("NodeId", "43");
        var after = engine.AnalyzeNodeDetailed(op, Ns);
        before.Diagnostics.Single().NodeId.Should().Be("42");
        before.Runs.Single().NodeId.Should().Be("42");
        before.ToLegacyResults().Single().NodeId.Should().Be("42");
        after.Diagnostics.Single().NodeId.Should().Be("43");
        after.Runs.Single().NodeId.Should().Be("43");
    }

    [Theory]
    [InlineData(RuleScope.Plan, RuleScope.Plan)]
    [InlineData(RuleScope.Statement, RuleScope.Statement)]
    [InlineData(RuleScope.Plan, RuleScope.Operator)]
    [InlineData(RuleScope.Operator, RuleScope.Plan)]
    [InlineData(RuleScope.Operator, RuleScope.Statement)]
    public void NodeId_FollowsDiagnosticAndInvocationScopesIndependently(RuleScope invocation, RuleScope diagnostic)
    {
        var rule = new NativeRule("TEST_SCOPE", c => RuleEvaluation.Hit(
            DiagnosticProtocolTests.Observation(c) with { Scope = diagnostic }), invocation);
        var report = Engine(rule).AnalyzePlanDetailed(DiagnosticProtocolTests.Plan(), Ns);
        report.HasFailures.Should().BeFalse();
        report.Runs.Single().NodeId.Should().Be(invocation == RuleScope.Operator ? "0" : null);
        report.Diagnostics.Single().NodeId.Should().Be(diagnostic == RuleScope.Operator ? "0" : null);
        report.ToLegacyResults().Single().NodeId.Should().Be(diagnostic == RuleScope.Operator ? "0" : "");
    }

    [Theory]
    [InlineData(RuleScope.Plan)]
    [InlineData(RuleScope.Statement)]
    public void SkippedScope_DoesNotBorrowStandaloneInvocationNodeId(RuleScope scope)
    {
        var rule = new NativeRule("TEST_SCOPE", _ => throw new InvalidOperationException("must not execute"), scope);
        var report = Engine(rule).AnalyzeNodeDetailed(Sample(detached: true), Ns);
        report.Runs.Single().Status.Should().Be(RuleRunStatus.Skipped);
        report.Runs.Single().NodeId.Should().BeNull();
    }

    [Fact]
    public void AllRunOutcomes_PreserveFragmentIdentityIncludingFailureJsonAndLegacyResult()
    {
        var reporter = new DiagnosticProtocolTests.RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(new NativeRule("TEST_IO", _ => throw new IOException("expected-storage-failure")));
        engine.RegisterRule(new NativeRule("TEST_NONE", _ => RuleEvaluation.NoHit()));
        engine.RegisterRule(new NativeRule("TEST_SKIP", _ => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "Missing.")));
        engine.RegisterRule(new NativeRule("TEST_SKIP_THROW", _ => throw new RuleSkippedException("RULE_MISSING_EVIDENCE", "Missing.")));
        var report = engine.AnalyzeNodeDetailed(Sample(detached: true), Ns);
        report.Runs.Select(r => r.Status).Should().Equal(RuleRunStatus.Failed, RuleRunStatus.NoHit, RuleRunStatus.Skipped, RuleRunStatus.Skipped);
        report.Runs.Should().OnlyContain(r => r.NodeId == "42");
        report.ToLegacyResults().Single().NodeId.Should().Be("42");
        DiagnosticTextFormatter.FormatRun(report.Runs[0]).Should().Contain("局部 NodeId：42");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report));
        json.RootElement.GetProperty("Runs")[0].GetProperty("NodeId").GetString().Should().Be("42");
        reporter.Operations.Should().BeEmpty();
    }

    internal static XElement Sample(bool function = false, bool detached = false)
    {
        var op = Op("EstimateRows='100'", Counter(function
            ? "ActualRows='10000' ActualExecutions='1'"
            : "ActualRows='10000' ActualRowsRead='100000' ActualExecutions='1'"),
            "<IndexScan>" + Predicate(function ? "YEAR([T].[D])=2026" : "[T].[V]=2") + "</IndexScan>");
        op.SetAttributeValue("NodeId", "42");
        if (detached) op.Remove();
        return op;
    }
}
