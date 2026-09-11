using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class WorkspaceSelectionHardeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualDeadlockSelection_ClearsEvidenceAndAllowsReturningToTheSameEvidence(bool resource)
    {
        var (vm, analysis) = Deadlock();
        vm.SelectPattern(analysis.Patterns.First(p => p.Evidence.Count > 0));
        var evidence = vm.SelectedEvidence!;
        var target = vm.SourceTarget!;
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        if (resource) vm.SelectResource(analysis.Resources.Last());
        else vm.SelectProcess(analysis.Processes.Single(p => p.Id != target.Process!.Id));
        vm.SelectedEvidence.Should().BeNull();
        vm.Evidence.Should().Contain(evidence);
        vm.SourceTarget!.EdgeId.Should().BeNull();
        notifications.Should().Contain(nameof(vm.SelectedEvidence));
        vm.SelectedEvidence = evidence;
        vm.SourceTarget.Should().BeEquivalentTo(target);
    }

    [Fact]
    public void ClearingEvidence_ClearsTargetAndCanSelectAgain()
    {
        var (vm, analysis) = Deadlock();
        vm.SelectPattern(analysis.Patterns.First(p => p.Evidence.Count > 0));
        var evidence = vm.SelectedEvidence!;
        vm.SelectedEvidence = null;
        vm.SourceTarget.Should().BeNull();
        vm.SelectedEvidence = evidence;
        vm.SourceTarget!.EdgeId.Should().Be(evidence.EdgeId);
    }

    [Fact]
    public void ReselectingEvidence_AfterSourceMutationRejectsStaleTarget()
    {
        var (vm, analysis) = Deadlock();
        vm.SelectPattern(analysis.Patterns.First(p => p.Evidence.Count > 0));
        var evidence = vm.SelectedEvidence;
        vm.SelectedEvent!.OriginalElement!.SetAttributeValue("changed", true);
        vm.SelectedEvidence = evidence;
        vm.SourceTarget.Should().BeNull();
        vm.Status.Should().Contain("已改变");
    }

    [Fact]
    public void Complete_AfterClearDoesNotReviveAnOldEvent()
    {
        var (vm, analysis) = Deadlock();
        var document = vm.SelectedEvent!.Document;
        vm.Clear();
        vm.Complete(document, analysis).Should().BeFalse();
        vm.Analysis.Should().BeNull();
        vm.SourceTarget.Should().BeNull();
    }

    [Theory]
    [InlineData(PlanLayoutMode.Horizontal)]
    [InlineData(PlanLayoutMode.Vertical)]
    public void RevealHiddenNode_RecalculatesCoordinatesBeforeMakingNodesVisible(PlanLayoutMode direction)
    {
        var input = GraphInput();
        var graph = WorkspaceSelectionTests.LoadGraph(input.Document!, PlanIdentityAdapter.GetOperatorSources(input.Document!, WorkspaceSelectionTests.Ns));
        var collapse = new PlanGraphCollapseUiActionService();
        var layout = new PlanGraphLayoutUiActionService();
        var nodes = graph.MasterNodes;
        nodes.Single(n => n.NodeId == "1").IsCollapsed = true;
        void Layout() => layout.ReapplyLayout(input.Document, WorkspaceSelectionTests.Ns, nodes, graph.MasterConnections, direction);
        void Visibility() => collapse.UpdateVisibility(input.Document, WorkspaceSelectionTests.Ns, nodes, graph.MasterConnections,
            nodes.ToList(), graph.MasterConnections.ToList());
        Layout(); Visibility();
        var target = nodes.Single(n => n.NodeId == "3");
        target.IsVisible.Should().BeFalse();
        collapse.RevealNode(target, nodes, Layout, () =>
        {
            target.Location.Should().NotBe(nodes.Single(n => n.NodeId == "5").Location);
            Visibility();
        }).Should().BeTrue();
        target.IsVisible.Should().BeTrue();
        nodes.Where(n => n.IsVisible).Select(n => n.Location).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void RevealNode_PreservesTargetAndUnrelatedCollapsedBranches()
    {
        var input = GraphInput();
        var graph = WorkspaceSelectionTests.LoadGraph(input.Document!, PlanIdentityAdapter.GetOperatorSources(input.Document!, WorkspaceSelectionTests.Ns));
        foreach (var node in graph.MasterNodes.Where(n => n.NodeId is "0" or "1" or "4")) node.IsCollapsed = true;
        var collapse = new PlanGraphCollapseUiActionService();
        collapse.RevealNode(graph.MasterNodes.Single(n => n.NodeId == "1"), graph.MasterNodes, () => { }, () => { });
        graph.MasterNodes.Single(n => n.NodeId == "0").IsCollapsed.Should().BeFalse();
        graph.MasterNodes.Single(n => n.NodeId == "1").IsCollapsed.Should().BeTrue();
        graph.MasterNodes.Single(n => n.NodeId == "4").IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void RevealVisibleNode_DoesNotRelayoutOrUndoUserPositions()
    {
        var input = GraphInput();
        var graph = WorkspaceSelectionTests.LoadGraph(input.Document!, PlanIdentityAdapter.GetOperatorSources(input.Document!, WorkspaceSelectionTests.Ns));
        var target = graph.MasterNodes.Single(n => n.NodeId == "1");
        target.IsCollapsed = true;
        target.Location = new(123, 456);
        new PlanGraphCollapseUiActionService().RevealNode(target, graph.MasterNodes,
            () => throw new InvalidOperationException("Visible selection must not relayout"),
            () => throw new InvalidOperationException("Visible selection must not alter visibility")).Should().BeFalse();
        target.Location.Should().Be(new System.Windows.Point(123, 456));
        target.IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void RevealForeignNode_RejectsItBeforeChangingCollapseState()
    {
        var input = GraphInput();
        var graph = WorkspaceSelectionTests.LoadGraph(input.Document!, PlanIdentityAdapter.GetOperatorSources(input.Document!, WorkspaceSelectionTests.Ns));
        var target = new PlanNodeViewModel { RawElement = graph.MasterNodes[0].RawElement, Identity = graph.MasterNodes[0].Identity };
        Action select = () => new PlanGraphCollapseUiActionService().RevealNode(target, graph.MasterNodes,
            () => throw new Exception("Unexpected layout"), () => throw new Exception("Unexpected visibility"));
        select.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("expected")]
    [InlineData("dump")]
    [InlineData("capture-failure")]
    public void RefactoringFailure_PropagatesFromAnalysisThroughUiToSelectedReport(string failure)
    {
        Exception error = failure == "expected" ? new IOException("Synthetic write denied") : new InvalidOperationException("Synthetic unknown write failure");
        var files = new FaultingFiles(error);
        var reporter = new Reporter(failure == "capture-failure");
        using var temporary = new TemporaryFileManager();
        var orchestrator = new ApplicationOrchestrator(new EmptyAnalysis(), new NoRefactoring(error), files,
            new SilentReporter(), NullLogger<ApplicationOrchestrator>.Instance);
        var service = new PlanAnalysisService(orchestrator, files, temporary, reporter,
            () => new RuleEngine(unexpectedErrors: reporter));
        var input = SingleInput();
        var analysis = service.Analyze(input.Document!, WorkspaceSelectionTests.Ns, "synthetic.sqlplan");
        analysis.RefactoringNotices.Should().Contain(error.Message);
        analysis.RefactoredSql.Should().Be("SELECT 1;");
        if (failure == "expected") reporter.Calls.Should().Be(0);
        else
        {
            reporter.Calls.Should().Be(1);
            analysis.RefactoringNotices.Should().Contain(failure == "dump" ? "synthetic.dmp" : "诊断组件失败");
        }
        RunSta(() =>
        {
            var model = new MainViewModel();
            var ui = new PlanAnalysisUiActionService(model, new TabControl(), new TabControl());
            ui.Apply(new(input.Document!, "synthetic.sqlplan", WorkspaceSelectionTests.Ns, analysis));
            model.PlanWorkspace.RefactoringNotices.Should().Be(analysis.RefactoringNotices);
            model.PlanWorkspace.SelectedReportText.Should().Contain(error.Message);
            new AnalysisClipboardService().BuildForTab(1, null, model.PlanWorkspace.SelectedReportText).Text.Should().Contain(error.Message);
        });
    }

    [Fact]
    public void RefactoringNotices_RemainDocumentScopedAndClearOnNewInput()
    {
        var input = WorkspaceSelectionTests.Input();
        var vm = new PlanWorkspaceViewModel();
        vm.Open(input.Document!, null, [], input, "多语句计划未自动改写；验证前提未满足。");
        vm.SelectedStatement = vm.Statements[1];
        vm.SelectedReportText.Should().Contain("文档分析结果").And.Contain("验证前提未满足");
        vm.Open(SingleInput().Document!, null, []);
        vm.HasRefactoringNotices.Should().BeFalse();
        vm.SelectedReportText.Should().NotContain("验证前提未满足");
        vm.Clear();
        vm.RefactoringNotices.Should().BeEmpty();
    }

    [Fact]
    public void FailedWorkspaceOpen_DoesNotReturnSuccessfulUiResult()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var ui = new PlanAnalysisUiActionService(model, new TabControl(), new TabControl());
            var document = SafeXmlHelper.ParseSafe("<invalid/>");
            Action apply = () => ui.Apply(new(document, "invalid.sqlplan", WorkspaceSelectionTests.Ns,
                new("", "SELECT 1", "", "", [], "SELECT 1")));
            apply.Should().Throw<InvalidDataException>().WithMessage("*工作区选择失败*");
            model.PlanWorkspace.Model.Should().BeNull();
            model.CurrentRewriteReview.Should().BeNull();
        });
    }

    internal static InputRecognitionResult GraphInput() => WorkspaceSelectionTests.Input("imp20_navigation_branches.sqlplan");

    private static InputRecognitionResult SingleInput() => new InputRecognitionService().Parse("""
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan"><BatchSequence><Batch><Statements>
          <StmtSimple StatementText="SELECT 1;"/>
        </Statements></Batch></BatchSequence></ShowPlanXML>
        """);

    private static (DeadlockWorkspaceViewModel Vm, DeadlockAnalysisOutput Analysis) Deadlock()
    {
        var input = WorkspaceSelectionTests.Input("deadlock_multiple_events.xdl");
        var vm = new DeadlockWorkspaceViewModel(new Reporter(false));
        vm.Begin(input, input.Deadlocks[1]);
        var analysis = new DeadlockAnalysisService().Analyze(input.Deadlocks[1].Document);
        vm.Complete(input.Deadlocks[1].Document, analysis);
        return (vm, analysis);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Reporter(bool fails) : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Calls++;
            if (fails) throw new IOException("Synthetic diagnostic provider failure");
            return new("synthetic.dmp", "synthetic.json", null);
        }
    }
    private sealed class FaultingFiles(Exception error) : IFileHandler
    {
        public Stream OpenRead(string path) => throw error;
        public string ReadAllText(string path) => throw error;
        public void WriteAllText(string path, string contents) => throw error;
        public bool Exists(string path) => false;
    }
    private sealed class EmptyAnalysis : IAnalysisEngine
    {
        public AnalysisReport Analyze(string xmlContent) => new(Array.Empty<IAnalysisIssue>());
    }
    private sealed class NoRefactoring(Exception? error = null) : IRefactoringEngine
    {
        public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun)
        {
            if (error != null) throw error;
            return new(sql, true, Array.Empty<string>(), new(sql));
        }
    }
    private sealed class SilentReporter : IResultReporter
    {
        public void Report(RefactorResult result) { }
        public void Report(RefactorResult result, bool isDryRun, string? outputPath) { }
    }
}
