using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using static SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests;
using static SqlXmlAnalyzer.Tests.RuleConfigurationDocumentTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class RuleConfigurationFlowTests
{
    private static RuleEngine EngineFor(RuleConfigurationDocument config)
    { var engine = new RuleEngine(configuration: config); engine.RegisterDefaultRules(); return engine; }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "Info")]
    [InlineData(true, "Warning")]
    [InlineData(true, "Critical")]
    public void Configuration_ChangesRealRuleResultsAndGraphUsesIdenticalDiagnostics(bool enabled, string? severity)
    {
        var doc = Plan(); var configuration = RuleConfigurationDocument.Defaults.WithSettings([new(Id, enabled, severity), new("RULE_030_CARDINALITY_ERROR", false, null)]);
        var report = EngineFor(configuration).AnalyzePlanDetailed(doc, Ns);
        var node = new PlanGraphNodeBuilderService(ruleEngine: Engine(new NativeRule("TEST_MUST_NOT_EXECUTE", _ => throw new Exception("graph reran rules"))))
            .Build(doc.Descendants(Ns + "RelOp").Single(), Ns, new(2, 100), report);
        node.Diagnostics!.Context.Should().BeSameAs(report.Context);
        node.Diagnostics.Runs.Should().OnlyContain(run => run.RuleId != "TEST_MUST_NOT_EXECUTE");
        node.Diagnostics.Diagnostics.Should().OnlyContain(diagnostic => report.Diagnostics.Contains(diagnostic));
        var diagnostics = report.Diagnostics.Where(d => d.RuleId == Id).ToArray();
        if (enabled)
        {
            diagnostics.Should().ContainSingle().Which.Severity.ToString().Should().Be(severity);
            node.Diagnostics.Diagnostics.Single(d => d.RuleId == Id).Should().BeSameAs(diagnostics[0]);
        }
        else
        {
            diagnostics.Should().BeEmpty(); report.Runs.Should().Contain(r => r.RuleId == Id && r.ReasonCode == "RULE_DISABLED");
            node.Diagnostics.Diagnostics.Should().NotContain(d => d.RuleId == Id);
        }
    }

    [Fact]
    public void Override_ExistingSemanticMergeKeepsOtherEnabledOriginsAndHighestSeverity()
    {
        var doc = Plan(); var config = RuleConfigurationDocument.Defaults.WithSettings([new(Id, true, "Info")]);
        var report = EngineFor(config).AnalyzePlanDetailed(doc, Ns);
        var diagnostic = report.Diagnostics.Single(d => d.RuleId == Id);
        diagnostic.Origins.Should().Contain(o => o.RuleId == "RULE_030_CARDINALITY_ERROR");
        diagnostic.Severity.Should().Be(IssueSeverity.Critical);
        report.Configuration[Id].SeverityOverride.Should().Be("Info");
        var both = EngineFor(config.WithSettings([new("RULE_030_CARDINALITY_ERROR", true, "Info")])).AnalyzePlanDetailed(doc, Ns);
        both.Diagnostics.Single(d => d.RuleId == Id).Severity.Should().Be(IssueSeverity.Info);
        var disabled = EngineFor(config.WithSettings([new(Id, false, null)])).AnalyzePlanDetailed(doc, Ns);
        disabled.Diagnostics.Should().Contain(d => d.RuleId == "RULE_030_CARDINALITY_ERROR");
    }

    [Fact]
    public void Apply_CapturedEngineAndWorkspaceRemainOldUntilExplicitReanalysis()
    {
        var session = new RuleConfigurationSession(RuleConfigurationDocument.Defaults);
        var capturedEngine = EngineFor(session.Capture()); var doc = Plan();
        var report = capturedEngine.AnalyzePlanDetailed(doc, Ns);
        var workspace = new PlanWorkspaceViewModel(); workspace.SetActiveConfiguration(session.Capture().Fingerprint); workspace.Open(doc, report, []);
        workspace.NeedsConfigurationReanalysis.Should().BeFalse();
        session.Apply(session.Capture().WithSettings([new(Id, false, null)])); workspace.SetActiveConfiguration(session.Capture().Fingerprint);
        workspace.NeedsConfigurationReanalysis.Should().BeTrue(); workspace.SelectedReportText.Should().Contain("需要重新分析");
        workspace.Report.Should().BeSameAs(report); workspace.Report.Configuration[Id].Enabled.Should().BeTrue();
        capturedEngine.AnalyzePlanDetailed(doc, Ns).Diagnostics.Should().Contain(d => d.RuleId == Id);
        var next = EngineFor(session.Capture()).AnalyzePlanDetailed(doc, Ns); workspace.Open(doc, next, []);
        workspace.NeedsConfigurationReanalysis.Should().BeFalse(); workspace.Issues.Should().NotContain(i => i.Diagnostic != null && i.Diagnostic.RuleId == Id);
        session.Apply(RuleConfigurationDocument.Defaults); workspace.SetActiveConfiguration(session.Capture().Fingerprint);
        workspace.Open(doc, EngineFor(session.Capture()).AnalyzePlanDetailed(doc, Ns), []);
        workspace.Issues.Should().Contain(i => i.Diagnostic != null && i.Diagnostic.RuleId == Id);
    }

    [Fact]
    public void Graph_RejectsChangedDocumentAndDistributesPlanStatementDiagnosticsWithoutRerunning()
    {
        var doc = Plan(operators: 2); int id = 0; foreach (var op in doc.Descendants(Ns + "RelOp")) op.SetAttributeValue("NodeId", id++);
        int calls = 0;
        var engine = Engine(new NativeRule("TEST_PLAN", c => { calls++; return RuleEvaluation.Hit(Observation(c)); }, RuleScope.Plan),
            new NativeRule("TEST_STMT", c => { calls++; return RuleEvaluation.Hit(Observation(c, "stmt")); }, RuleScope.Statement));
        var report = engine.AnalyzePlanDetailed(doc, Ns); calls.Should().Be(2);
        var first = doc.Descendants(Ns + "RelOp").First(); var last = doc.Descendants(Ns + "RelOp").Last();
        report.ForOperator(first).Diagnostics.Should().HaveCount(2); report.ForOperator(last).Diagnostics.Should().BeEmpty();
        report.ForOperator(first).Should().BeSameAs(report.ForOperator(first)); calls.Should().Be(2);
        first.SetAttributeValue("EstimateRows", "200");
        ((Action)(() => report.ForOperator(first))).Should().Throw<InvalidDataException>();
    }
}
