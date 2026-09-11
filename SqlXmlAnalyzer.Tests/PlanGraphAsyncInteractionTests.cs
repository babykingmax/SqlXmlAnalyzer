using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows;
using Nodify;
using System.Windows.Threading;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanGraphAsyncInteractionTests
{
    [Fact]
    public void Navigation_WhenViewportWasPannedElsewhere_BringsTheNewPageIntoView() => RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        graph.Measure(new Size(900, 600)); graph.Arrange(new Rect(0, 0, 900, 600));
        var (input, prepared) = Prepare(graph, 130);
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        var editor = (NodifyEditor)graph.FindName("Editor");
        editor.ViewportLocation = new Point(50000, 50000);
        await graph.NavigatePageAsync(64);
        var anchor = graph.Nodes[0].Location;
        var viewport = new Rect(editor.ViewportLocation, new Size(editor.ActualWidth / editor.ViewportZoom, editor.ActualHeight / editor.ViewportZoom));
        viewport.Contains(new Point(anchor.X + 115, anchor.Y + 55)).Should().BeTrue();
    });

    [Fact]
    public void SynchronousReload_WhenAsyncPageWasPending_DoesNotKeepTheCancelledPageTask() => RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        var (inputA, preparedA) = Prepare(graph, 130);
        var (inputB, _) = Prepare(graph, 2);
        var old = graph.ApplyPreparedAsync(inputA.Document!, InputRecognitionService.ShowPlanNamespace, preparedA, CancellationToken.None);
        graph.LoadFromExecutionPlan(inputB.Document!, InputRecognitionService.ShowPlanNamespace);
        Func<Task> finishOld = () => old;
        await finishOld.Should().ThrowAsync<OperationCanceledException>();
        await graph.PendingPage;
        graph.Nodes.Should().HaveCount(2);
    });

    [Fact]
    public void InitialCommit_WhenPagesAreReplaced_WaitsForTheLatestPage() => RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        var (input, prepared) = Prepare(graph, 130);
        var initial = graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        var second = graph.NavigatePageAsync(64);
        var last = graph.NavigatePageAsync(128);
        await Task.WhenAll(initial, second, last);
        graph.AllNodes.Should().HaveCount(130);
        graph.Nodes.Should().Equal(prepared.MasterNodes.Skip(128));
        graph.PendingPage.IsCompletedSuccessfully.Should().BeTrue();
    });

    [Fact]
    public void LayoutChange_WhenGraphIsPaged_UpdatesConnectionsOnAllPages() => RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        var (input, prepared) = Prepare(graph, 130);
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        graph.LayoutMode = PlanLayoutMode.Vertical;
        await graph.PendingPage;
        prepared.MasterConnections.Should().OnlyContain(c => c.LayoutMode == PlanLayoutMode.Vertical && c.ArrowAngle == -90);
        await graph.NavigatePageAsync(64);
        prepared.MasterConnections.Should().OnlyContain(c => c.LayoutMode == PlanLayoutMode.Vertical);
    });

    [Theory]
    [InlineData(2)]
    [InlineData(130)]
    public void Commit_WhenOptionsChangedAfterPreparation_UsesCurrentOptions(int count) => RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        var (input, prepared) = Prepare(graph, count);
        ((ComboBox)graph.FindName("CmbViewMode")).SelectedIndex = (int)DiagramViewMode.Rows;
        graph.ColorMode = PlanColorMode.CpuCost;
        graph.LinkMetric = LinkMetricMode.DataSize;
        graph.LayoutMode = PlanLayoutMode.Vertical;
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        graph.AllNodes.Should().OnlyContain(n => n.ViewMode == DiagramViewMode.Rows && n.ColorMode == PlanColorMode.CpuCost);
        prepared.MasterConnections.Should().OnlyContain(c => c.CurrentLinkMetric == LinkMetricMode.DataSize && c.LayoutMode == PlanLayoutMode.Vertical);
        graph.Nodes[1].Location.Y.Should().BeGreaterThan(graph.Nodes[0].Location.Y);
    });

    [Fact]
    public void Navigation_WhenCompletedRequestIsCancelled_CanStillCommitAnotherPage() => RunSta(async () =>
    {
        using var cancellation = new CancellationTokenSource();
        var graph = new PlanGraphControl();
        var (input, prepared) = Prepare(graph, 130);
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, cancellation.Token);
        cancellation.Cancel();
        await graph.NavigatePageAsync(64);
        graph.Nodes.Should().Equal(prepared.MasterNodes.Skip(64).Take(64));
        graph.AllNodes.Should().HaveCount(130);
        graph.PendingPage.IsCompletedSuccessfully.Should().BeTrue();
    });

    [Fact]
    public void InitialCommit_WhenCancelledAfterPageReplacement_PropagatesCancellation() => RunSta(async () =>
    {
        using var cancellation = new CancellationTokenSource();
        var graph = new PlanGraphControl();
        var (input, prepared) = Prepare(graph, 130);
        var initial = graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, cancellation.Token);
        var page = graph.NavigatePageAsync(64);
        cancellation.Cancel();
        Func<Task> finish = () => initial;
        await finish.Should().ThrowAsync<OperationCanceledException>();
        await page;
        graph.Nodes.Should().BeEmpty();
    });

    [Fact]
    public void Commit_WhenAnotherDocumentReplacesIt_RejectsTheOldPage() => RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        var (inputA, preparedA) = Prepare(graph, 130);
        var (inputB, preparedB) = Prepare(graph, 2);
        var old = graph.ApplyPreparedAsync(inputA.Document!, InputRecognitionService.ShowPlanNamespace, preparedA, CancellationToken.None);
        await graph.ApplyPreparedAsync(inputB.Document!, InputRecognitionService.ShowPlanNamespace, preparedB, CancellationToken.None);
        Func<Task> finishOld = () => old;
        await finishOld.Should().ThrowAsync<OperationCanceledException>();
        graph.Nodes.Should().Equal(preparedB.MasterNodes);
        graph.AllNodes.Should().OnlyContain(n => ReferenceEquals(n.RawElement!.Document, inputB.Document));
    });

    private static (InputRecognitionResult Input, PlanGraphLoadUiActionResult Graph) Prepare(PlanGraphControl graph, int count)
    {
        string Leaf(int id) => $"<RelOp NodeId='{id}' PhysicalOp='Constant Scan' LogicalOp='Constant Scan' EstimateRows='1'/>";
        string operators = $"<RelOp NodeId='0' PhysicalOp='Concatenation' LogicalOp='Concatenation' EstimateRows='1'><Concat>{string.Concat(Enumerable.Range(1, count - 1).Select(Leaf))}</Concat></RelOp>";
        var input = new InputRecognitionService().Parse(PlanIdentityModelTests.Wrap($"<StmtSimple StatementText='SELECT 1'><QueryPlan>{operators}</QueryPlan></StmtSimple>"));
        var nodes = PlanIdentityAdapter.GetOperatorSources(input.Document!, InputRecognitionService.ShowPlanNamespace);
        var options = graph.CaptureLoadOptions(nodes, null, [], CancellationToken.None);
        var prepared = new PlanGraphLoadUiActionService().Load(input.Document!, InputRecognitionService.ShowPlanNamespace,
            new ObservableCollection<PlanNodeViewModel>(), new ObservableCollection<ConnectionViewModel>(), options);
        return (input, prepared);
    }

    internal static void RunSta(Func<Task> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                var task = test();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!task.IsCompleted)
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("WPF interaction test timed out.");
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                }
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(25)).Should().BeTrue();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
