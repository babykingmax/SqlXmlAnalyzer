using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using Nodify;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanGraphViewportInteractionTests
{
    [Fact]
    public void Fit_WhenRequestedBeforeFirstArrange_AppliesWithoutASecondClick() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        graph.Nodes.Add(new PlanNodeViewModel { Location = new Point(100, 200) });
        graph.Nodes.Add(new PlanNodeViewModel { Location = new Point(2300, 1400) });
        graph.FitCurrentGraph();
        Arrange(graph);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        AssertGraphFits(graph);
        var editor = Editor(graph);
        double firstZoom = editor.ViewportZoom;
        var firstLocation = editor.ViewportLocation;
        graph.FitCurrentGraph();
        editor.ViewportZoom.Should().BeApproximately(firstZoom, 0.000001);
        editor.ViewportLocation.Should().Be(firstLocation);
        return Task.CompletedTask;
    });

    [Fact]
    public void Fit_WhenSixtyFourNodesMeetATinyViewport_UsesOnlyTheNecessaryExtraZoomRange() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        var editor = Editor(graph);
        editor.Height = 24;
        for (int i = 0; i < 64; i++)
            graph.Nodes.Add(new PlanNodeViewModel { NodeId = i.ToString(), Location = new Point(50, 80 + 120 * i) });
        Arrange(graph);
        editor.ActualHeight.Should().Be(24, "padding must also adapt when the viewport is shorter than 32 DIP");
        graph.FitCurrentGraph();
        AssertGraphFits(graph);
        double fitZoom = editor.ViewportZoom;
        Point fitLocation = editor.ViewportLocation;
        fitZoom.Should().BeGreaterThan(0).And.BeLessThan(0.02);
        double.IsFinite(fitZoom).Should().BeTrue();
        graph.FitCurrentGraph();
        editor.ViewportZoom.Should().BeApproximately(fitZoom, 0.000000001);
        editor.ViewportLocation.Should().Be(fitLocation);
        graph.ZoomOut();
        editor.ViewportZoom.Should().Be(fitZoom, "manual zoom-out stops at the range opened by Fit");
        graph.ZoomIn();
        editor.ViewportZoom.Should().BeApproximately(fitZoom * 1.2, 0.000000001);
        graph.ResetZoom();
        editor.ViewportZoom.Should().Be(1);
        editor.MinViewportZoom.Should().Be(0.02, "returning to a normal scale closes the extra fit range");
        return Task.CompletedTask;
    });

    [Fact]
    public void Fit_WhenCurrentPageIsStillCommitting_FitsTheCompletePageOnce() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        Arrange(graph);
        var (input, prepared) = Prepare(graph, 130);
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        var pending = graph.NavigatePageAsync(64);
        pending.IsCompleted.Should().BeFalse();
        graph.FitCurrentGraph();
        await pending;
        graph.Nodes.Should().HaveCount(64);
        AssertGraphFits(graph);
        double zoom = Editor(graph).ViewportZoom;
        Point location = Editor(graph).ViewportLocation;
        graph.FitCurrentGraph();
        Editor(graph).ViewportZoom.Should().BeApproximately(zoom, 0.000001);
        Editor(graph).ViewportLocation.Should().Be(location);
    });

    [Fact]
    public void ZoomButtons_WhenClickedRapidly_KeepTheSameGraphPointAtTheViewportCenter() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        Arrange(graph);
        var editor = Editor(graph);
        editor.ViewportLocation = new Point(2400, 1700);
        Point center = Center(editor);
        void Click(string label)
        {
            var button = WorkspaceAccessibility.Descendants(graph).OfType<Button>().Single(b => Equals(b.Content, label));
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Center(editor).X.Should().BeApproximately(center.X, 0.000001);
            Center(editor).Y.Should().BeApproximately(center.Y, 0.000001);
            Point rendered = editor.ViewportTransform.Transform(center);
            rendered.X.Should().BeApproximately(editor.ActualWidth / 2, 0.000001);
            rendered.Y.Should().BeApproximately(editor.ActualHeight / 2, 0.000001);
        }
        Click("+");
        editor.ViewportZoom.Should().BeApproximately(1.2, 0.000001);
        Click("+");
        editor.ViewportZoom.Should().BeApproximately(1.44, 0.000001);
        Click("−");
        editor.ViewportZoom.Should().BeApproximately(1.2, 0.000001);
        Click("100%");
        editor.ViewportZoom.Should().Be(1);
        return Task.CompletedTask;
    });

    [Fact]
    public void Zoom_WhenNodifyPanAnimationHasNotTicked_AppliesImmediatelyAndRestoresInteraction() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        Arrange(graph);
        var editor = Editor(graph);
        editor.BringIntoView(new Point(9000, 7000), animated: true);
        // WPF need not report IsAnimated until its first animation tick, but
        // Nodify has already disabled input. A click in this interval must
        // cancel the queued animation rather than leave controls disabled.
        editor.IsPanning.Should().BeTrue();
        editor.DisablePanning.Should().BeTrue();
        editor.DisableZooming.Should().BeTrue();
        graph.ZoomIn();
        editor.ViewportZoom.Should().BeApproximately(1.2, 0.000001);
        DependencyPropertyHelper.GetValueSource(editor, NodifyEditor.ViewportLocationProperty).IsAnimated.Should().BeFalse();
        editor.DisablePanning.Should().BeFalse();
        editor.DisableZooming.Should().BeFalse();
        editor.IsPanning.Should().BeFalse();
        return Task.CompletedTask;
    });

    [Fact]
    public void Zoom_WhenAtEitherLimit_TheNextOppositeClickRespondsImmediately() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        Arrange(graph);
        var editor = Editor(graph);
        editor.MinViewportZoom.Should().Be(0.02, "the Nodify 6 default coercion otherwise silently clamps the minimum to 10%");
        for (int i = 0; i < 40; i++) graph.ZoomOut();
        editor.ViewportZoom.Should().Be(0.02);
        graph.ZoomIn();
        editor.ViewportZoom.Should().BeApproximately(0.024, 0.000001);
        for (int i = 0; i < 40; i++) graph.ZoomIn();
        editor.ViewportZoom.Should().Be(editor.MaxViewportZoom);
        graph.ZoomOut();
        editor.ViewportZoom.Should().BeApproximately(editor.MaxViewportZoom / 1.2, 0.000001);
        return Task.CompletedTask;
    });

    [Fact]
    public void ResetZoom_WhenFitWasQueuedDuringPaging_CancelsTheOlderViewportIntent() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        Arrange(graph);
        var (input, prepared) = Prepare(graph, 130);
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        var pending = graph.NavigatePageAsync(64);
        graph.FitCurrentGraph();
        graph.ResetZoom();
        Point location = Editor(graph).ViewportLocation;
        await pending;
        Editor(graph).ViewportZoom.Should().Be(1);
        Editor(graph).ViewportLocation.Should().Be(location);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MouseWheel_WhenFitIsQueuedDuringPaging_PreservesTheLatestMouseZoom(bool atLimit) => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        Arrange(graph);
        var (input, prepared) = Prepare(graph, 130);
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        if (atLimit) for (int i = 0; i < 40; i++) graph.ZoomIn();
        var pending = graph.NavigatePageAsync(64);
        graph.FitCurrentGraph();
        var editor = Editor(graph);
        double previousZoom = editor.ViewportZoom;
        var previousModifier = EditorGestures.Mappings.Editor.ZoomModifierKey;
        try
        {
            EditorGestures.Mappings.Editor.ZoomModifierKey = Keyboard.Modifiers;
            editor.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
                { RoutedEvent = Mouse.MouseWheelEvent, Source = editor });
        }
        finally { EditorGestures.Mappings.Editor.ZoomModifierKey = previousModifier; }
        double mouseZoom = editor.ViewportZoom;
        Point mouseLocation = editor.ViewportLocation;
        if (atLimit) mouseZoom.Should().Be(editor.MaxViewportZoom);
        else mouseZoom.Should().BeGreaterThan(previousZoom, "the event must pass through Nodify's actual wheel handler");
        await pending;
        editor.ViewportZoom.Should().Be(mouseZoom);
        editor.ViewportLocation.Should().Be(mouseLocation);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PointerMove_WhenFitIsQueuedDuringPaging_OnlyAnActualPanSupersedesIt(bool dragging) => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        Arrange(graph);
        var (input, prepared) = Prepare(graph, 130);
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        var pending = graph.NavigatePageAsync(64);
        graph.FitCurrentGraph();
        var editor = (PlanGraphEditor)Editor(graph);
        Point previousLocation = editor.ViewportLocation;
        // Use the same pointer calculation and viewport callback as the real
        // PreviewMouseMove path, with deterministic graph pointer positions.
        new PlanGraphPanUiActionService().Pan(new PlanGraphPanState(dragging, new Point(100, 100)),
            new Point(150, 180), editor.ViewportLocation, editor.ViewportZoom, editor.PanViewport);
        Point mouseLocation = editor.ViewportLocation;
        double mouseZoom = editor.ViewportZoom;
        if (dragging) mouseLocation.Should().NotBe(previousLocation);
        else mouseLocation.Should().Be(previousLocation);
        await pending;
        if (dragging)
        {
            editor.ViewportLocation.Should().Be(mouseLocation);
            editor.ViewportZoom.Should().Be(mouseZoom);
        }
        else AssertGraphFits(graph);
    });

    [Fact]
    public void Locate_WhenNodeIsOffPage_CentersTheFinalLayoutWithoutChangingZoom() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        Arrange(graph);
        var (input, prepared) = Prepare(graph, 130);
        await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
        graph.ResetZoom();
        var node = graph.AllNodes[100];
        graph.SelectOperator(node.Identity!);
        await graph.PendingPage;
        var editor = Editor(graph);
        editor.ViewportZoom.Should().Be(1);
        Point center = Center(editor);
        center.X.Should().BeApproximately(node.Location.X + PlanGraphNodeMetrics.Width / 2, 1);
        center.Y.Should().BeApproximately(node.Location.Y + PlanGraphNodeMetrics.Height / 2, 1);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrossPageFocus_WhenUserThenClicksFitOrReset_DoesNotOverrideTheNewViewportOrStealFocus(bool fit) => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var graph = new PlanGraphControl();
        var window = new Window { Content = graph, Width = 900, Height = 600, Left = -20000, Top = 0,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            window.Show(); window.Activate(); window.UpdateLayout();
            var (input, prepared) = Prepare(graph, 130);
            await graph.ApplyPreparedAsync(input.Document!, InputRecognitionService.ShowPlanNamespace, prepared, CancellationToken.None);
            graph.SelectedNode = graph.AllNodes[100];
            graph.LocateSelectedNode();
            graph.PendingPage.IsCompleted.Should().BeFalse();
            string label = fit ? "适应图" : "100%";
            var button = WorkspaceAccessibility.Descendants(graph).OfType<Button>().Single(b => Equals(b.Content, label));
            WorkspaceAccessibility.Focus(button).Should().BeTrue();
            int laterNodeFocusAttempts = 0;
            graph.PreviewGotKeyboardFocus += (_, e) =>
            {
                if (e.NewFocus is AccessiblePlanNode) laterNodeFocusAttempts++;
            };
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Point resetLocation = Editor(graph).ViewportLocation;
            await graph.PendingPage;
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            laterNodeFocusAttempts.Should().Be(0, "the older LocateSelectedNode continuation must be superseded by the toolbar action");
            button.IsKeyboardFocusWithin.Should().BeTrue();
            if (fit) AssertGraphFits(graph);
            else
            {
                Editor(graph).ViewportZoom.Should().Be(1);
                Editor(graph).ViewportLocation.Should().Be(resetLocation);
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanvasPan_WhenInputComesFromNodeInlineOrButton_DoesNotStealMouseCapture(bool button) => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var node = new PlanNodeViewModel();
        var text = new TextBlock { DataContext = node };
        var inline = new Run("估算 ");
        text.Inlines.Add(inline);
        var parent = new Border { DataContext = node };
        var control = new Button { Content = text, DataContext = new object() };
        parent.Child = control;
        bool captured = false;
        var initial = new PlanGraphPanState(false, new Point());
        var result = new PlanGraphPanUiActionService().BeginPan(button ? control : inline,
            new Point(20, 30), () => captured = true, initial);
        captured.Should().BeFalse();
        result.Should().Be(initial);
        return Task.CompletedTask;
    });

    private static void Arrange(PlanGraphControl graph)
    {
        graph.Measure(new Size(900, 600));
        graph.Arrange(new Rect(0, 0, 900, 600));
        graph.UpdateLayout();
    }

    private static NodifyEditor Editor(PlanGraphControl graph) => (NodifyEditor)graph.FindName("Editor");

    private static Point Center(NodifyEditor editor) => new(editor.ViewportLocation.X + editor.ActualWidth / (2 * editor.ViewportZoom),
        editor.ViewportLocation.Y + editor.ActualHeight / (2 * editor.ViewportZoom));

    private static void AssertGraphFits(PlanGraphControl graph)
    {
        var editor = Editor(graph);
        graph.UpdateLayout();
        double horizontalPadding = Math.Min(16, editor.ActualWidth / 4) - 0.1;
        double verticalPadding = Math.Min(16, editor.ActualHeight / 4) - 0.1;
        foreach (var node in graph.Nodes.Where(n => n.IsVisible))
        {
            var container = (FrameworkElement)editor.ItemContainerGenerator.ContainerFromItem(node);
            Point start = editor.ViewportTransform.Transform(node.Location);
            Point end = editor.ViewportTransform.Transform(new Point(node.Location.X + container.ActualWidth, node.Location.Y + container.ActualHeight));
            start.X.Should().BeGreaterThanOrEqualTo(horizontalPadding);
            start.Y.Should().BeGreaterThanOrEqualTo(verticalPadding);
            end.X.Should().BeLessThanOrEqualTo(editor.ActualWidth - horizontalPadding);
            end.Y.Should().BeLessThanOrEqualTo(editor.ActualHeight - verticalPadding);
        }
    }

    private static (InputRecognitionResult Input, PlanGraphLoadUiActionResult Graph) Prepare(PlanGraphControl graph, int count)
    {
        string leaves = string.Concat(Enumerable.Range(1, count - 1).Select(id => $"<RelOp NodeId='{id}' PhysicalOp='Constant Scan' EstimateRows='1'/>"));
        var input = new InputRecognitionService().Parse(PlanIdentityModelTests.Wrap($"<StmtSimple StatementText='SELECT 1'><QueryPlan><RelOp NodeId='0' PhysicalOp='Concatenation' EstimateRows='1'><Concat>{leaves}</Concat></RelOp></QueryPlan></StmtSimple>"));
        var operators = PlanIdentityAdapter.GetOperatorSources(input.Document!, InputRecognitionService.ShowPlanNamespace);
        var options = graph.CaptureLoadOptions(operators, null, [], CancellationToken.None);
        var prepared = new PlanGraphLoadUiActionService().Load(input.Document!, InputRecognitionService.ShowPlanNamespace,
            new ObservableCollection<PlanNodeViewModel>(), new ObservableCollection<ConnectionViewModel>(), options);
        return (input, prepared);
    }
}
