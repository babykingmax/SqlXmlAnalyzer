using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;
using static SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class WorkspaceSelectionTests
{
    internal static XNamespace Ns => InputRecognitionService.ShowPlanNamespace;
    internal static InputRecognitionResult Input(string fixture = "imp14_cardinality_residual.sqlplan") =>
        new InputRecognitionService().Parse(InputRecognitionTests.Fixture(fixture));
    internal static PlanDiagnosticReport Report(XDocument document)
    {
        var engine = new RuleEngine(unexpectedErrors: new RecordingReporter());
        engine.RegisterRule(new NativeRule("TEST_IMP20", c => RuleEvaluation.Hit(new DiagnosticProposal("TEST_IMP20", "读取与输出证据", "统一指标", Core.Abstractions.IssueSeverity.Warning)
        {
            SemanticCode = "TEST_IMP20", Title = "读取与输出证据", Summary = "统一指标",
            Severity = Core.Abstractions.IssueSeverity.Warning, Confidence = DiagnosticConfidence.High,
            Evidence = [new("OutputRows", c.Facts!.OutputRows.Display(), "RunTimeCountersPerThread/@ActualRows", c.Location),
                new("RowsRead", c.Facts.RowsRead.Display(), "RunTimeCountersPerThread/@ActualRowsRead", c.Location)]
        })));
        return engine.AnalyzePlanDetailed(document, Ns);
    }

    [Fact]
    public void StatementSelection_SynchronizesSqlGraphTreesIssuesAndOriginalIdentity()
    {
        var input = Input(); var doc = input.Document!; string before = doc.ToString();
        var vm = new PlanWorkspaceViewModel(); vm.Open(doc, Report(doc), [], input);
        var first = vm.Selection!;
        vm.SelectedStatement = vm.Statements[1];
        var second = vm.Selection!;
        first.Operators.Single().Attribute("NodeId")!.Value.Should().Be(second.Operators.Single().Attribute("NodeId")!.Value);
        first.Choice.Statement.Key.Should().NotBe(second.Choice.Statement.Key);
        second.Choice.Statement.Text.Should().Contain("FROM U");
        second.Issues.Should().OnlyContain(i => i.Location.Statement == second.Choice.Statement.Key);
        var graph = LoadGraph(doc, second.Operators);
        graph.MasterNodes.Single().Identity.Should().Be(second.Choice.QueryPlan!.Operators.Single().Key);
        graph.MasterNodes.Single().RawElement.Should().BeSameAs(second.Operators.Single());
        graph.MasterNodes.Single().Facts!.OutputRows.Display().Should().Be("10000");
        graph.MasterNodes.Single().Facts!.RowsRead.Display().Should().Be("100000");
        new PlanTreeService().BuildScopedVisualTree(second.Operators, Ns).Single().Tag.Should().BeSameAs(second.Operators.Single());
        new PlanTreeService().BuildScopedOperatorTree(second.Operators, Ns).Single().Source.Should().BeSameAs(second.Operators.Single());
        vm.SelectedIssue = second.Issues.Single(); vm.SelectedEvidence = vm.Evidence[1];
        vm.SourceTarget!.Location.Operator.Should().Be(graph.MasterNodes.Single().Identity);
        vm.SourceTarget.Sql.Should().Contain("FROM U"); vm.SourceTarget.Xml.Should().Contain("[U]").And.NotContain("[T]");
        vm.SourceTarget.Description.Should().Contain("StmtSimple[2]");
        vm.SelectedIssue!.Severity.Should().Be("Warning"); vm.SelectedIssue.Confidence.Should().Be("High");
        vm.SelectedReportText.Should().Contain(vm.SelectedIssue.Diagnostic!.DiagnosticId).And.Contain("RowsRead");
        doc.ToString().Should().Be(before);
    }

    [Fact]
    public void BatchAndQueryPlanSelection_IncludeStatementsWithoutOperatorsAndClearOldEvidence()
    {
        var input = Input("imp10_scoped_identities.sqlplan"); var vm = new PlanWorkspaceViewModel();
        vm.Open(input.Document!, Report(input.Document!), [], input);
        vm.SelectedIssue = vm.Issues[0]; vm.SourceTarget.Should().NotBeNull();
        vm.SelectedBatch = vm.Batches[1];
        vm.Selection!.Choice.Statement.Kind.Should().Be("StmtUseDb");
        vm.Selection.Operators.Should().BeEmpty(); vm.Status.Should().Contain("未采集");
        vm.SourceTarget.Should().BeNull(); vm.SelectedIssue.Should().BeNull();
        vm.Statements.Should().HaveCount(3);
        vm.SelectedStatement = vm.Statements[2];
        vm.Selection!.Choice.QueryPlan!.Key.QueryPlanOrdinal.Should().Be(2);
        vm.Selection.Operators.Should().ContainSingle();
    }

    [Fact]
    public void MultipleRootsAndNestedStatements_DoNotBorrowChildStatementOperators()
    {
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap("<StmtSimple StatementText='outer'><QueryPlan><RelOp NodeId='0'/><RelOp NodeId='0'/></QueryPlan><UDF><Statements><StmtSimple StatementText='inner'><QueryPlan><RelOp NodeId='0'/></QueryPlan></StmtSimple></Statements></UDF></StmtSimple>"));
        var vm = new PlanWorkspaceViewModel(); vm.Open(document, Report(document), []);
        vm.Selection!.Operators.Should().HaveCount(2);
        new PlanTreeService().BuildScopedOperatorTree(vm.Selection.Operators, Ns).Should().HaveCount(2);
        LoadGraph(document, vm.Selection.Operators).MasterNodes.Select(n => n.Identity).Should().OnlyHaveUniqueItems();
        vm.SelectedStatement = vm.Statements[1];
        vm.Selection!.Operators.Should().ContainSingle(); vm.Selection.Choice.Statement.Text.Should().Be("inner");
    }

    [Fact]
    public void DocumentDiagnostic_CanNavigateEvidenceInAnotherBatchWithoutLocalNodeIdGuessing()
    {
        var input = Input("imp10_scoped_identities.sqlplan"); var document = input.Document!;
        var engine = new RuleEngine();
        engine.RegisterRule(new NativeRule("TEST_DOCUMENT", c => RuleEvaluation.Hit(new DiagnosticProposal("DOC", "全局证据", "跨语句", Core.Abstractions.IssueSeverity.Warning)
        {
            SemanticCode = "DOC", Title = "全局证据", Summary = "跨语句", Scope = RuleScope.Plan,
            Evidence = c.Analysis.Model.Operators.Select(o => new DiagnosticEvidence("node", o.Key.NodeId, "RelOp", o.Location)).ToArray()
        }), RuleScope.Plan));
        var vm = new PlanWorkspaceViewModel(); vm.Open(document, engine.AnalyzePlanDetailed(document, Ns), [], input);
        vm.SelectedIssue = vm.Issues.Single();
        var evidence = vm.Evidence.Last(); vm.SelectedEvidence = evidence;
        vm.SelectedBatch!.Key.Should().Be(evidence.Location.Batch);
        vm.SelectedStatement!.QueryPlan!.Key.Should().Be(evidence.Location.QueryPlan);
        vm.SelectedIssue!.Scope.Should().Be("文档"); vm.SourceTarget!.Location.Should().Be(evidence.Location);
        vm.Evidence.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("NoHit")]
    [InlineData("Skipped")]
    [InlineData("Failed")]
    public void EmptyAndIncompleteDiagnostics_ExposeActualRunState(string state)
    {
        var input = Input(); var engine = new RuleEngine(unexpectedErrors: new RecordingReporter());
        engine.RegisterRule(new NativeRule("TEST_STATE", _ => state switch
        {
            "NoHit" => RuleEvaluation.NoHit(), "Skipped" => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "未采集实际行"),
            _ => throw new IOException("Synthetic expected input failure")
        }));
        var vm = new PlanWorkspaceViewModel(); vm.Open(input.Document!, engine.AnalyzePlanDetailed(input.Document!, Ns), []);
        vm.Status.Should().Contain(state + " 1");
        if (state == "NoHit") { vm.Issues.Should().BeEmpty(); vm.Status.Should().Contain("不代表"); }
        else { vm.Issues.Should().ContainSingle(); vm.SelectedIssue = vm.Issues[0]; vm.Details.Should().Contain(state); }
    }

    [Fact]
    public void MissingReportAndLongSql_AreNotHiddenOrTruncated()
    {
        string sql = "SELECT '" + new string('x', 2000) + "'";
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap("<StmtSimple/>"));
        document.Descendants(Ns + "StmtSimple").Single().SetAttributeValue("StatementText", sql);
        var vm = new PlanWorkspaceViewModel(); vm.Open(document, null, []);
        vm.SelectedStatement!.Statement.Text.Should().Be(sql); vm.Status.Should().Contain("尚无规则执行记录");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("foreign-report")]
    [InlineData("old-issue")]
    [InlineData("foreign-batch")]
    public void StaleOrForeignSelection_IsRejectedAndClearedWithoutDump(string fault)
    {
        var input = Input(); var recorder = new RecordingReporter(); var vm = new PlanWorkspaceViewModel(recorder);
        vm.Open(input.Document!, Report(input.Document!), []); var issue = vm.Issues[0];
        if (fault == "source") { input.Document!.Root!.SetAttributeValue("Build", "changed"); vm.SelectedIssue = issue; }
        if (fault == "foreign-report") vm.Open(input.Document!, Report(Input().Document!), []);
        if (fault == "old-issue") { vm.SelectedStatement = vm.Statements[1]; vm.SelectedIssue = issue; }
        if (fault == "foreign-batch") vm.SelectedBatch = PlanIdentityAdapter.GetDocument(Input().Document)!.Batches[0];
        vm.Selection.Should().BeNull(); vm.SourceTarget.Should().BeNull(); vm.Issues.Should().BeEmpty();
        vm.Status.Should().Contain("失败"); recorder.Operations.Should().BeEmpty();
    }

    [Fact]
    public void MissingIndexes_FollowExactStatementAndQueryPlanScope()
    {
        var input = Input("imp10_scoped_identities.sqlplan"); var doc = input.Document!;
        var indexes = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, Ns);
        var vm = new PlanWorkspaceViewModel(); vm.Open(doc, Report(doc), indexes);
        foreach (var choice in vm.Statements.ToArray())
        {
            vm.SelectedStatement = choice;
            vm.Selection!.MissingIndexes.Should().OnlyContain(i => i.Location!.QueryPlan == choice.QueryPlan!.Key);
        }
        vm.Selection!.MissingIndexes.Should().NotBeEmpty();
    }

    [Fact]
    public void DeadlockEventSelection_RetainsOriginalEventAndRawEvidenceAndClearsOnSwitch()
    {
        var input = new InputRecognitionService().Parse(InputRecognitionTests.Fixture("deadlock_multiple_events.xdl"));
        var vm = new DeadlockWorkspaceViewModel(); var first = input.Deadlocks[0]; var second = input.Deadlocks[1];
        vm.Begin(input, first); var analysis = new DeadlockAnalysisService().Analyze(first.Document);
        vm.Complete(first.Document, analysis).Should().BeTrue();
        vm.SelectProcess(analysis.Processes[0]);
        vm.SourceTarget!.Xml.Should().Contain(analysis.Processes[0].Id);
        vm.SourceTarget.Location!.EventIndex.Should().Be(first.Index);
        vm.Begin(input, second);
        vm.Analysis.Should().BeNull(); vm.Evidence.Should().BeEmpty(); vm.SourceTarget.Should().BeNull();
        vm.Complete(first.Document, analysis).Should().BeFalse();
        var next = new DeadlockAnalysisService().Analyze(second.Document); vm.Complete(second.Document, next).Should().BeTrue();
        vm.SelectPattern(next.Patterns.First(p => p.Evidence.Count > 0));
        vm.SourceTarget.Should().NotBeNull(); vm.SourceTarget!.Location!.XmlPath.Should().StartWith(second.Location!.XmlPath);
        vm.SourceTarget.Location.XmlPath.Should().Contain("event[2]");
        vm.SourceTarget.Location.Line.Should().NotBeNull();
        var dependency = next.Graph.Edges.Single(edge => edge.EdgeId == vm.SourceTarget.EdgeId);
        vm.SourceTarget.LinkIds.Should().BeEquivalentTo(new[] { dependency.WaiterLinkId, dependency.OwnerLinkId });
        vm.SourceTarget.ProcessIds.Should().BeEquivalentTo(new[] { dependency.FromProcessId, dependency.ToProcessId });
        vm.Context.Should().Contain(second.DisplayName); vm.Nature.Should().Contain("不是真实回放");
    }

    [Fact]
    public void SameProcessIdsInDifferentEvents_CannotReuseOldDiagnostics()
    {
        var xml = "<deadlock><process-list><process id='p' spid='1' priority='0'/></process-list><resource-list><keylock id='r'><owner-list><owner id='p' mode='S'/></owner-list></keylock></resource-list></deadlock>";
        var input = new InputRecognitionService().Parse("<deadlock-list>" + xml + xml + "</deadlock-list>");
        var reporter = new RecordingReporter(); var vm = new DeadlockWorkspaceViewModel(reporter);
        vm.Begin(input, input.Deadlocks[0]); var first = new DeadlockAnalysisService().Analyze(input.Deadlocks[0].Document);
        vm.Complete(input.Deadlocks[0].Document, first); var old = first.Patterns[0];
        vm.Begin(input, input.Deadlocks[1]); vm.Complete(input.Deadlocks[1].Document, new DeadlockAnalysisService().Analyze(input.Deadlocks[1].Document));
        vm.SelectPattern(old);
        vm.Status.Should().Contain("不属于当前事件"); vm.SourceTarget.Should().BeNull(); reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void IdenticalResourceNames_ResolveTheExactDisplayedResource()
    {
        var first = new LockResource("keylock", "T", "I1", "h1", "1", [], [], "r1");
        var second = first with { Id = "r2", IndexName = "I2" };
        var details = new Dictionary<string, (string, string)> { ["res_single_1"] = ("keylock", "T"), ["res_single_99"] = ("keylock", "T") };
        var service = new DeadlockGraphSelectionService();
        service.FindResourceForNode("res_single_1", details, [first, second]).Should().BeSameAs(second);
        service.FindResourceForNode("res_single_99", details, [first, second]).Should().BeNull();
    }

    [Fact]
    public void ClearResults_ReleasesBothWorkspaceSelections()
    {
        var model = new MainViewModel(); var input = Input();
        model.PlanWorkspace.Open(input.Document!, Report(input.Document!), [], input);
        var deadlocks = new InputRecognitionService().Parse(InputRecognitionTests.Fixture("deadlock_multiple_events.xdl"));
        model.DeadlockWorkspace.Begin(deadlocks, deadlocks.Deadlocks[0]);
        model.ClearResults();
        model.PlanWorkspace.Document.Should().BeNull(); model.DeadlockWorkspace.SelectedEvent.Should().BeNull();
    }

    [Fact]
    public void RelayoutOfSelectedQueryPlan_DoesNotReserveSpaceForOtherStatements()
    {
        var input = Input(); var vm = new PlanWorkspaceViewModel(); vm.Open(input.Document!, Report(input.Document!), []);
        vm.SelectedStatement = vm.Statements[1];
        var graph = LoadGraph(input.Document!, vm.Selection!.Operators);
        var original = graph.MasterNodes.Single().Location;
        new PlanGraphLayoutUiActionService().ReapplyLayout(input.Document, Ns, graph.MasterNodes, graph.MasterConnections, PlanLayoutMode.Horizontal);
        graph.MasterNodes.Single().Location.Should().Be(original);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeadlockSourceMutation_PreventsNavigationToStaleEvidence(bool originalSource)
    {
        var input = new InputRecognitionService().Parse(InputRecognitionTests.Fixture("deadlock_multiple_events.xdl"));
        var vm = new DeadlockWorkspaceViewModel(new RecordingReporter()); var selected = input.Deadlocks[0];
        vm.Begin(input, selected); var analysis = new DeadlockAnalysisService().Analyze(selected.Document); vm.Complete(selected.Document, analysis);
        (originalSource ? selected.OriginalElement! : selected.Document.Root!).SetAttributeValue("changed", "true");
        vm.SelectProcess(analysis.Processes[0]);
        vm.SourceTarget.Should().BeNull(); vm.Status.Should().Contain("已改变");
    }

    [Fact]
    public void SelectingNode_UpdatesDiagnosticDetailsToTheSameNode()
    {
        var input = Input("imp10_scoped_identities.sqlplan"); var vm = new PlanWorkspaceViewModel(); vm.Open(input.Document!, Report(input.Document!), []);
        vm.SelectedIssue = vm.Issues[0]; vm.NavigateOperator(vm.Selection!.Operators.Last());
        vm.SelectedIssue!.Location.Operator.Should().Be(vm.SourceTarget!.Location.Operator);
    }

    internal static PlanGraphLoadUiActionResult LoadGraph(XDocument document, IReadOnlyList<XElement> operators) =>
        new PlanGraphLoadUiActionService().Load(document, Ns, new ObservableCollection<PlanNodeViewModel>(),
            new ObservableCollection<ConnectionViewModel>(), new PlanGraphLoadUiActionOptions
            {
                Operators = operators, InitialLayout = PlanLayoutMode.Horizontal, InitialColor = PlanColorMode.TotalCost,
                InitialView = DiagramViewMode.Rows, InitialLinkMetric = LinkMetricMode.RowCount,
                ResidualIoThreshold = 2, ResidualIoMinRowsRead = 100
            });
}
