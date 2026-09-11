using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Views;
using static SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanWorkspacePresentationTests
{
    [Fact]
    public void MixedDiagnosticsAndIncompleteChecks_AppearInSeparateCollectionsWithoutChangingReport()
    {
        var document = Plan();
        var engine = Engine();
        engine.RegisterRule(new NativeRule("TEST_FINDING", context => RuleEvaluation.Hit(Proposal(context, "finding", IssueSeverity.Warning))));
        engine.RegisterRule(new NativeRule("TEST_SKIP", context => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "未采集运行数据")));
        engine.RegisterRule(new NativeRule("TEST_FAIL", context => throw new InvalidOperationException("synthetic check failure")));
        var report = engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns);
        var original = DiagnosticTextFormatter.Format(report);
        var vm = Open(document, report);

        vm.Findings.Should().HaveCount(2).And.OnlyContain(issue => issue.Diagnostic != null);
        vm.CheckRuns.Should().HaveCount(4).And.OnlyContain(issue => issue.Diagnostic == null && issue.Run != null);
        vm.Issues.Should().HaveCount(6);
        vm.CheckRuns.Take(2).Should().OnlyContain(issue => issue.Run!.Status == RuleRunStatus.Failed);
        vm.AnalysisSummary.Should().Be("2 个问题 · 2 项未执行 · 2 项检查失败");
        vm.HasFailedChecks.Should().BeTrue();
        vm.SelectedIssue = vm.CheckRuns[0];
        vm.DetailTitle.Should().Be("检查未完成");
        vm.DetailSubtitle.Should().Contain("检查失败").And.NotContain("Critical");
        DiagnosticTextFormatter.Format(report).Should().Be(original);
        vm.SelectedReportText.Should().Contain("synthetic check failure");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThousandsOfSkippedChecks_DoNotCrowdFindingsOrProduceHealthyEmptyState(bool hasFinding)
    {
        var document = Plan();
        var engine = Engine();
        for (var index = 0; index < 1000; index++)
            engine.RegisterRule(new NativeRule($"TEST_SKIP_{index}", context => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "未采集")));
        if (hasFinding)
            engine.RegisterRule(new NativeRule("TEST_ONE_FINDING", context => context.Operator!.Key.OperatorOrdinal == 1
                ? RuleEvaluation.Hit(Proposal(context, "需要调查的唯一问题", IssueSeverity.Warning)) : RuleEvaluation.NoHit()));
        var vm = Open(document, engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns));

        vm.CheckRuns.Should().HaveCount(2000);
        vm.HasFailedChecks.Should().BeFalse();
        vm.Findings.Should().HaveCount(hasFinding ? 1 : 0);
        vm.FilteredFindings.Should().HaveCount(hasFinding ? 1 : 0);
        if (hasFinding) vm.FilteredFindings.Single().Title.Should().Be("需要调查的唯一问题");
        else vm.FindingsEmptyMessage.Should().Contain("部分检查未完成");
        vm.AnalysisSummary.Should().Contain("2000 项未执行");
        vm.AnalysisSummary.Length.Should().BeLessThan(100);
    }

    [Fact]
    public void Findings_PrioritizeSeverityThenConfidenceThenOriginalOperatorOrder()
    {
        var document = Plan();
        var engine = Engine();
        engine.RegisterRule(new NativeRule("TEST_SORT", context => context.Operator!.Key.OperatorOrdinal == 1
            ? RuleEvaluation.Hit(Proposal(context, "warning high first", IssueSeverity.Warning, DiagnosticConfidence.High),
                Proposal(context, "critical low", IssueSeverity.Critical, DiagnosticConfidence.Low))
            : RuleEvaluation.Hit(Proposal(context, "warning low", IssueSeverity.Warning, DiagnosticConfidence.Low),
                Proposal(context, "warning high second", IssueSeverity.Warning, DiagnosticConfidence.High),
                Proposal(context, "critical high", IssueSeverity.Critical, DiagnosticConfidence.High))));
        var vm = Open(document, engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns));

        vm.Findings.Select(issue => issue.Title).Should().Equal("critical high", "critical low", "warning high first", "warning high second", "warning low");
    }

    [Fact]
    public void SearchAndSeverityFilter_CombineWithoutFilteringTheOriginalFindingsOrCheckRuns()
    {
        var document = Plan();
        var engine = Engine();
        engine.RegisterRule(new NativeRule("TEST_SEARCH", context => RuleEvaluation.Hit(
            Proposal(context, "Row estimate", IssueSeverity.Warning), Proposal(context, "row critical", IssueSeverity.Critical))));
        engine.RegisterRule(new NativeRule("TEST_SKIP", context => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "未采集")));
        var vm = Open(document, engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns));
        vm.SearchText = "  ROW  ";
        vm.SeverityFilter = "Warning";

        vm.FilteredFindings.Should().HaveCount(2).And.OnlyContain(issue => issue.Title == "Row estimate");
        vm.Findings.Should().HaveCount(4);
        vm.CheckRuns.Should().HaveCount(2);
        vm.SearchText = "does not exist";
        vm.FilteredFindings.Should().BeEmpty();
        vm.FindingsEmptyMessage.Should().Contain("筛选");
        vm.SearchText = "TEST_SEARCH";
        vm.SeverityFilter = "全部";
        vm.FilteredFindings.Should().HaveCount(4);
    }

    [Fact]
    public void SelectingNodeWithoutFinding_PreservesAllFindingsAndShowsNodeFacts()
    {
        var document = Plan();
        var engine = Engine();
        engine.RegisterRule(new NativeRule("TEST_ONE", context => context.Operator!.Key.OperatorOrdinal == 1
            ? RuleEvaluation.Hit(Proposal(context, "first only", IssueSeverity.Warning)) : RuleEvaluation.NoHit()));
        var vm = Open(document, engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns));
        var all = vm.Findings;
        vm.SelectedIssue = all.Single();
        vm.NavigateOperator(vm.Selection!.Operators.Last());

        vm.Findings.Should().BeSameAs(all);
        vm.FilteredFindings.Should().ContainSingle();
        vm.RelatedFindings.Should().BeEmpty();
        vm.SelectedIssue.Should().BeNull();
        vm.SourceTarget!.Location.Operator.Should().Be(vm.Selection.Choice.QueryPlan!.Operators.Last().Key);
        vm.DetailTitle.Should().Be("Index Scan");
        vm.DetailFacts.Should().NotBeEmpty();
    }

    [Fact]
    public void RelatedFindings_UseCompleteIdentityForDuplicateNodeIdsAndClearOnStatementSwitch()
    {
        var document = Plan(twoStatements: true);
        var engine = Engine();
        engine.RegisterRule(new NativeRule("TEST_ALL", context => RuleEvaluation.Hit(Proposal(context, "same title", IssueSeverity.Warning))));
        var vm = Open(document, engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns));
        vm.NavigateOperator(vm.Selection!.Operators.Last());

        vm.RelatedFindings.Should().ContainSingle();
        vm.RelatedFindings[0].Location.Operator.Should().Be(vm.Selection.Choice.QueryPlan!.Operators.Last().Key);
        vm.Findings.Should().HaveCount(2);
        vm.SelectedStatement = vm.Statements[1];
        vm.RelatedFindings.Should().BeEmpty();
        vm.SourceTarget.Should().BeNull();
        vm.SelectedIssue.Should().BeNull();
        vm.DetailTitle.Should().Be("当前语句概览");
        vm.DetailFacts.Should().Contain(fact => fact.Name == "SQL 简要");
    }

    [Theory]
    [InlineData(null, "未采集")]
    [InlineData("0", "0")]
    public void NodeFacts_DistinguishUnknownRuntimeRowsFromMeasuredZero(string? actualRows, string expected)
    {
        var counters = actualRows == null ? "" : $"<RunTimeInformation><RunTimeCountersPerThread Thread='0' ActualRows='{actualRows}' ActualExecutions='1'/></RunTimeInformation>";
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
            $"<StmtSimple><QueryPlan><RelOp NodeId='1' PhysicalOp='Index Scan' EstimateRows='0'>{counters}</RelOp></QueryPlan></StmtSimple>"));
        var vm = Open(document, null);
        vm.NavigateOperator(vm.Selection!.Operators[0]);

        vm.DetailFacts.Single(fact => fact.Name == "实际输出行数（总计）").Value.Should().StartWith(expected);
        vm.DetailFacts.Single(fact => fact.Name == "估算输出行数（每次执行）").Value.Should().StartWith("0");
        vm.DetailFacts.Single(fact => fact.Name == "实际读取行数（总计）").Value.Should().Be("未采集");
    }

    [Fact]
    public void PlanDataSummary_UsesRuntimeCoverageOfTheCurrentStatement()
    {
        const string counters = "<RunTimeInformation><RunTimeCountersPerThread Thread='0' ActualRows='0' ActualExecutions='1'/></RunTimeInformation>";
        static string Statement(string parentCounters, string childCounters) =>
            $"<StmtSimple StatementText='SELECT 1'><QueryPlan><RelOp NodeId='1' EstimateRows='1'>{parentCounters}<NestedLoops><RelOp NodeId='2' EstimateRows='1'>{childCounters}</RelOp></NestedLoops></RelOp></QueryPlan></StmtSimple>";
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(Statement("", "") + Statement(counters, counters) + Statement(counters, "")));
        var vm = Open(document, null);

        vm.FileSummary.Should().Contain("仅估算信息");
        vm.SelectedStatement = vm.Statements[1];
        vm.FileSummary.Should().Contain("含实际运行信息").And.NotContain("仅估算");
        vm.SelectedStatement = vm.Statements[2];
        vm.FileSummary.Should().Contain("部分运行计数");
        vm.DetailSubtitle.Should().Contain("部分运行计数");
    }

    [Fact]
    public void UnselectedOperator_ShowsStatementSummaryAndCheckCompleteness()
    {
        var document = Plan();
        var engine = Engine();
        engine.RegisterRule(new NativeRule("TEST_FINDING", context => RuleEvaluation.Hit(Proposal(context, "finding", IssueSeverity.Warning))));
        engine.RegisterRule(new NativeRule("TEST_SKIP", context => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "未采集")));
        var vm = Open(document, engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns));

        vm.DetailTitle.Should().Be("当前语句概览");
        vm.DetailFacts.Should().Contain(new PlanWorkspaceFact("SQL 简要", "SELECT * FROM T"));
        vm.DetailFacts.Should().Contain(new PlanWorkspaceFact("算子数", "2"));
        vm.DetailFacts.Should().Contain(new PlanWorkspaceFact("已发现问题", "2"));
        vm.DetailFacts.Single(fact => fact.Name == "检查完整性").Value.Should().Contain("2 项未执行").And.Contain("检查未完成");
        vm.Clear();
        vm.DetailTitle.Should().Be("选择问题或算子");
        vm.DetailFacts.Should().BeEmpty();
    }

    [Fact]
    public void ElapsedTimeFact_IsMaximumThreadTimeRatherThanSum()
    {
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
            "<StmtSimple><QueryPlan><RelOp NodeId='1' PhysicalOp='Index Scan'><RunTimeInformation>"
            + "<RunTimeCountersPerThread Thread='1' ActualRows='1' ActualElapsedms='9'/>"
            + "<RunTimeCountersPerThread Thread='2' ActualRows='1' ActualElapsedms='4'/>"
            + "</RunTimeInformation></RelOp></QueryPlan></StmtSimple>"));
        var vm = Open(document, null);
        vm.NavigateOperator(vm.Selection!.Operators[0]);

        vm.DetailFacts.Single(fact => fact.Name == "实际耗时（线程最大值）").Value.Should().Be("9 ms");
    }

    [Fact]
    public void StructuredDetail_PreservesFactsHypothesesRecommendationsAndRawEvidence()
    {
        var document = Plan();
        var engine = Engine();
        engine.RegisterRule(new NativeRule("TEST_DETAIL", context => RuleEvaluation.Hit(
            Proposal(context, "detail", IssueSeverity.Warning) with
            {
                Summary = "观察到估算偏差", Hypotheses = ["统计信息可能过期"], Recommendations = ["检查统计信息"],
                Limitations = ["未采集实际行数"], Evidence = [new("ActualRows", null, "@ActualRows", context.Location, State: PlanMetricState.Missing)]
            })));
        var vm = Open(document, engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns));
        vm.SelectedIssue = vm.Findings[0];

        vm.DetailFacts.Should().Contain(new PlanWorkspaceFact("观察", "观察到估算偏差"));
        vm.DetailReasons.Should().Be("统计信息可能过期");
        vm.DetailRecommendations.Should().Be("检查统计信息");
        vm.DetailLimitations.Should().Be("限制：未采集实际行数");
        vm.Evidence.Single().Value.Should().BeNull();
        vm.SourceDetails.Should().Contain("TEST_DETAIL").And.Contain("@ActualRows");
        vm.FileSummary.Should().NotContain(vm.Model!.Envelope.DocumentId);
    }

    [Fact]
    public void DocumentEvidence_InTheProductionViewKeepsTheCrossBatchTargetWithRepeatedNodeIds() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (view, model) = CreateDocumentEvidenceView();
        ArrangeEvidenceView(view);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        var workspace = model.PlanWorkspace;
        var grid = (DataGrid)view.FindName("DiagnosticIssuesGrid");
        grid.SetCurrentValue(Selector.SelectedItemProperty, workspace.Findings.Single(issue => issue.Title == "document first"));
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        var target = workspace.Evidence.Last();
        var selector = (ComboBox)view.FindName("DiagnosticEvidenceSelector");
        selector.SetCurrentValue(Selector.SelectedItemProperty, target);
        ArrangeEvidenceView(view);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);

        workspace.SelectedBatch!.Key.Should().Be(target.Location.Batch);
        workspace.SelectedStatement!.QueryPlan!.Key.Should().Be(target.Location.QueryPlan);
        workspace.SelectedEvidence.Should().BeSameAs(target);
        workspace.SourceTarget!.Location.Should().Be(target.Location);
        workspace.Findings.Should().HaveCount(2);
        workspace.RelatedFindings.Should().HaveCount(2);
        selector.SelectedItem.Should().BeSameAs(target);
        view.DataContext = null;
    });

    [Fact]
    public void EquivalentDiagnosticWrapperSelection_PreservesEvidenceButADifferentDiagnosticStillChangesIt() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (view, model) = CreateDocumentEvidenceView();
        ArrangeEvidenceView(view);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        var workspace = model.PlanWorkspace;
        workspace.SelectedIssue = workspace.Findings.Single(issue => issue.Title == "document first");
        workspace.SelectedEvidence = workspace.Evidence.Last();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        var original = workspace.SelectedIssue!;
        var evidence = workspace.SelectedEvidence!;
        var target = workspace.SourceTarget!;
        var replacement = original with { };
        replacement.Should().NotBeSameAs(original).And.Be(original);
        var grid = (DataGrid)view.FindName("DiagnosticIssuesGrid");

        // Reproduce WPF reporting an equal replacement item after a scope rebuild,
        // through the actual routed event and the production selection handler.
        grid.RaiseEvent(new SelectionChangedEventArgs(Selector.SelectionChangedEvent, new[] { original }, new[] { replacement }));
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        workspace.SelectedEvidence.Should().BeSameAs(evidence);
        workspace.SourceTarget.Should().BeSameAs(target);

        var different = workspace.Findings.Single(issue => issue.Title == "document second");
        grid.SetCurrentValue(Selector.SelectedItemProperty, different);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        workspace.SelectedIssue!.Diagnostic!.DiagnosticId.Should().Be(different.Diagnostic!.DiagnosticId);
        workspace.SelectedEvidence.Should().BeNull();
        workspace.SourceTarget!.Location.Should().Be(different.Location);
        workspace.DetailTitle.Should().Be("document second");
        view.DataContext = null;
    });

    private static (PlanWorkspaceView View, MainViewModel Model) CreateDocumentEvidenceView()
    {
        if (System.Windows.Application.Current == null)
        {
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string key in new[] { "MaterialDesignFlatButton", "MaterialDesignRaisedButton", "MaterialDesignOutlinedButton", "SecondaryButtonStyle" })
            {
                var style = new Style(typeof(Button));
                style.Seal();
                application.Resources[key] = style;
            }
        }
        const string batch = "<Batch><Statements><StmtSimple StatementText='SELECT 1'><QueryPlan><RelOp NodeId='7' PhysicalOp='Index Scan' EstimateRows='1'/></QueryPlan></StmtSimple></Statements></Batch>";
        var document = SafeXmlHelper.ParseSafe($"<ShowPlanXML xmlns='{PlanIdentityModelTests.Ns}'><BatchSequence>{batch}{batch}</BatchSequence></ShowPlanXML>");
        var engine = Engine();
        engine.RegisterRule(new NativeRule("TEST_DOCUMENT_VIEW", context =>
        {
            DiagnosticProposal DocumentProposal(string title) => new("TEST_" + title, title, title, IssueSeverity.Warning)
            {
                Scope = RuleScope.Plan,
                Evidence = context.Analysis.Model.Operators.Select(op => new DiagnosticEvidence("node", op.Key.NodeId, "RelOp", op.Location)).ToArray()
            };
            return RuleEvaluation.Hit(DocumentProposal("document first"), DocumentProposal("document second"));
        }, RuleScope.Plan));
        var model = new MainViewModel();
        model.PlanWorkspace.Open(document, engine.AnalyzePlanDetailed(document, PlanIdentityModelTests.Ns), []);
        var view = new PlanWorkspaceView
        {
            DataContext = model,
            PreferencesStore = new PlanWorkspacePreferencesStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "SqlXmlAnalyzer-evidence-selection-tests", Guid.NewGuid().ToString("N") + ".json"))
        };
        view.ApplyPreferences(new());
        return (view, model);
    }

    private static void ArrangeEvidenceView(FrameworkElement view)
    {
        view.Measure(new Size(1500, 700));
        view.Arrange(new Rect(0, 0, 1500, 700));
        view.UpdateLayout();
    }

    private static RuleEngine Engine() => new(unexpectedErrors: new RecordingReporter());
    private static DiagnosticProposal Proposal(RuleAnalysisContext context, string title, IssueSeverity severity, DiagnosticConfidence confidence = DiagnosticConfidence.High) =>
        new("TEST_" + title, title, title + " summary", severity)
        {
            Confidence = confidence,
            Evidence = [new("EstimateRows", context.Facts!.EstimatedRows.Display(), "@EstimateRows", context.Location)]
        };
    private static PlanWorkspaceViewModel Open(XDocument document, PlanDiagnosticReport? report)
    {
        var vm = new PlanWorkspaceViewModel(new RecordingReporter());
        vm.Open(document, report, []);
        return vm;
    }
    private static XDocument Plan(bool twoStatements = false)
    {
        const string statement = "<StmtSimple StatementText='SELECT * FROM T'><QueryPlan><RelOp NodeId='7' PhysicalOp='Nested Loops' EstimateRows='2'><NestedLoops><RelOp NodeId='7' PhysicalOp='Index Scan' EstimateRows='2'/></NestedLoops></RelOp></QueryPlan></StmtSimple>";
        return SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(statement + (twoStatements ? statement : "")));
    }
}
