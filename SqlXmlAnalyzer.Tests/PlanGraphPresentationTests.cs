using FluentAssertions;
using SqlXmlAnalyzer.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using static SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanGraphPresentationTests
{
    [Theory]
    [InlineData(false, IssueSeverity.Critical, "CriticalSurfaceBrush", "CriticalBrush")]
    [InlineData(true, IssueSeverity.Critical, "CriticalSurfaceBrush", "CriticalBrush")]
    [InlineData(false, IssueSeverity.Warning, "WarningSurfaceBrush", "WarningBrush")]
    [InlineData(true, IssueSeverity.Warning, "WarningSurfaceBrush", "WarningBrush")]
    [InlineData(false, IssueSeverity.Info, "SelectionBrush", "InfoBrush")]
    [InlineData(true, IssueSeverity.Info, "SelectionBrush", "InfoBrush")]
    public void GraphTheme_SeparatesSelectedStateFromDiagnosticSeverity(bool dark, IssueSeverity severity, string surface, string foreground) => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        WorkspaceThemeService.Apply(graph.Resources, dark);
        var palette = WorkspaceThemeService.Palette(dark);
        var document = Plan();
        var report = Engine(new NativeRule("TEST_BADGE", c => RuleEvaluation.Hit(Observation(c, severity: severity))))
            .AnalyzePlanDetailed(document, Ns);
        graph.LoadFromExecutionPlan(document, Ns, diagnostics: report);
        graph.Measure(new Size(900, 600)); graph.Arrange(new Rect(0, 0, 900, 600)); graph.UpdateLayout();
        var badge = WorkspaceAccessibility.Descendants(graph).OfType<Border>().Single(b => b.Name == "DiagnosticBadge");
        ((SolidColorBrush)badge.Background).Color.Should().Be(palette[surface]);
        ((SolidColorBrush)((TextBlock)badge.Child).Foreground).Color.Should().Be(palette[foreground]);
        var editor = (Nodify.NodifyEditor)graph.FindName("Editor");
        var container = (Nodify.ItemContainer)editor.ItemContainerGenerator.ContainerFromItem(graph.Nodes[0]);
        ((SolidColorBrush)container.SelectedBrush).Color.Should().Be(palette["AccentBrush"]);
        container.SelectedBorderThickness.Should().Be(new Thickness(2));
        var settings = (Button)graph.FindName("DisplaySettingsButton");
        ((SolidColorBrush)settings.Background).Color.Should().Be(palette["SurfaceBrush"]);
        ((SolidColorBrush)settings.Foreground).Color.Should().Be(palette["TextBrush"]);
        settings.ActualHeight.Should().Be(28);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapseButton_RoutedClickAndEnterToggleExactlyOnce(bool keyboard) => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        var parent = InputFieldMappingTests.RelOp("<NestedLoops><RelOp NodeId='1' PhysicalOp='Constant Scan' EstimateRows='1'/></NestedLoops>");
        parent.SetAttributeValue("NodeId", "0");
        var operators = parent.DescendantsAndSelf().Where(e => e.Name.LocalName == "RelOp").ToArray();
        graph.LoadFromExecutionPlan(parent.Document!, parent.Name.Namespace, operators);
        graph.Measure(new Size(900, 600)); graph.Arrange(new Rect(0, 0, 900, 600)); graph.UpdateLayout();
        var node = graph.AllNodes.Single(n => n.NodeId == "0");
        var button = WorkspaceAccessibility.Descendants(graph).OfType<Button>()
            .Single(b => b.Name == "CollapseNodeButton" && ReferenceEquals(b.DataContext, node));
        var presentation = new KeyboardPresentationSource { RootVisual = graph };
        void Activate()
        {
            if (keyboard)
                button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentation, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            else button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            graph.UpdateLayout();
        }
        Activate();
        node.IsCollapsed.Should().BeTrue();
        graph.Nodes.Where(n => n.IsVisible).Should().ContainSingle().Which.Should().BeSameAs(node);
        graph.AllNodes.Single(n => n.NodeId == "1").IsVisible.Should().BeFalse();
        Activate();
        node.IsCollapsed.Should().BeFalse();
        graph.Nodes.Should().HaveCount(2);
        graph.Nodes.Should().OnlyContain(n => n.IsVisible);
        return Task.CompletedTask;
    });

    private sealed class KeyboardPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    [Fact]
    public void FitCurrentGraph_ExcludesCollapsedDescendantsRetainedInTheCollection() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        graph.Nodes.Add(new PlanNodeViewModel { NodeId = "0", Location = new Point(10, 10) });
        graph.Nodes.Add(new PlanNodeViewModel { NodeId = "1", IsVisible = false, Location = new Point(100000, 100000) });
        graph.Measure(new Size(900, 600)); graph.Arrange(new Rect(0, 0, 900, 600)); graph.UpdateLayout();
        graph.FitCurrentGraph();
        ((Nodify.NodifyEditor)graph.FindName("Editor")).ViewportZoom.Should().Be(1, "only the small visible graph should determine the viewport");
        return Task.CompletedTask;
    });

    [Fact]
    public void Preferences_AutomaticModeUsesAvailableEvidenceButExplicitModeSurvivesReload() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        int changes = 0;
        graph.PreferencesChanged += (_, _) => changes++;
        var op = InputFieldMappingTests.RelOp("""
            <RunTimeInformation><RunTimeCountersPerThread ActualRows="1000" ActualExecutions="10" /></RunTimeInformation>
            """);
        var document = op.Document!;
        graph.LoadFromExecutionPlan(document, op.Name.Namespace, new[] { op });
        graph.CapturePreferences().ViewMode.Should().Be(DiagramViewMode.Rows);
        graph.CapturePreferences().HasExplicitViewMode.Should().BeFalse();
        changes.Should().Be(0);
        graph.RestorePreferences(new Core.Services.PlanGraphPreferences { ViewMode = DiagramViewMode.CostPercent, HasExplicitViewMode = true });
        graph.LoadFromExecutionPlan(document, op.Name.Namespace, new[] { op });
        graph.AllNodes.Should().OnlyContain(n => n.ViewMode == DiagramViewMode.CostPercent);
        changes.Should().Be(0);
        return Task.CompletedTask;
    });

    [Fact]
    public void ZoomedOut_HidesMetricsAndLabelsWhilePreservingSelection() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        var editor = (Nodify.NodifyEditor)graph.FindName("Editor");
        var selected = new PlanNodeViewModel { NodeId = "7" };
        graph.SelectedNode = selected;
        editor.ViewportZoom = 0.4;
        graph.NodeMetricsVisible.Should().BeFalse();
        graph.ConnectionLabelsVisible.Should().BeFalse();
        graph.ResetZoom();
        graph.NodeMetricsVisible.Should().BeTrue();
        graph.ConnectionLabelsVisible.Should().BeTrue();
        graph.SelectedNode.Should().BeSameAs(selected);
        return Task.CompletedTask;
    });

    [Fact]
    public void Search_IncludesOffPageAndCollapsedNodes_WithoutCaseSensitivity()
    {
        var nodes = new[]
        {
            new PlanNodeViewModel { NodeId = "1", PhysicalOp = "Sort" },
            new PlanNodeViewModel { NodeId = "65", PhysicalOp = "Index Seek", TableName = "[Orders]", IsVisible = false }
        };
        PlanGraphPresentationService.Search(nodes, "orders").Should().ContainSingle().Which.Should().BeSameAs(nodes[1]);
        PlanGraphPresentationService.Search(nodes, "#65").Should().ContainSingle().Which.Should().BeSameAs(nodes[1]);
        PlanGraphPresentationService.Search(nodes, "  ").Should().BeEmpty();
    }

    [Fact]
    public void Boundaries_KeepBothDirectionsWithoutDuplicatingInPageEdges()
    {
        var parent = new PlanNodeViewModel { NodeId = "0" };
        var current = new PlanNodeViewModel { NodeId = "63" };
        var child = new PlanNodeViewModel { NodeId = "64" };
        var sibling = new PlanNodeViewModel { NodeId = "62" };
        var edges = new[]
        {
            new ConnectionViewModel { Source = current, Target = parent },
            new ConnectionViewModel { Source = child, Target = current },
            new ConnectionViewModel { Source = sibling, Target = current }
        };
        var boundaries = PlanGraphPresentationService.Boundaries(edges, new[] { current, sibling });
        boundaries.Should().HaveCount(2);
        boundaries.Select(b => b.Destination).Should().BeEquivalentTo(new[] { parent, child });
        boundaries.Select(b => b.Direction).Should().BeEquivalentTo(new[] { "输出到", "输入来自" });
        edges[1].IsVisible = false;
        PlanGraphPresentationService.Boundaries(edges, new[] { current, sibling }).Should().ContainSingle();
    }

    [Theory]
    [InlineData(100, 10, "0.1×")]
    [InlineData(100, 0, "0×")]
    [InlineData(0, 10, "估算为 0")]
    [InlineData(0, 0, "均为 0")]
    [InlineData(1000000, 1, "1E-6×")]
    public void RowComparison_HandlesObservedZeroWithoutInventingInfinity(double estimate, double actual, string ratio)
    {
        var node = new PlanNodeViewModel { HasActualRows = true, EstRowsNum = estimate, ActualRowsNum = actual, ViewMode = DiagramViewMode.Rows };
        node.RowEstimateRatioDisplay.Should().Be(ratio);
        node.PrimaryDisplayValue.Should().Contain("实 ").And.Contain(" / 估 ").And.Contain(ratio);
        if (estimate == 0) node.RowEstimateRatio.Should().BeNull();
    }

    [Fact]
    public void RowComparison_UsesPerExecutionFactsRatherThanTotalRows()
    {
        var op = InputFieldMappingTests.RelOp("""
            <RunTimeInformation><RunTimeCountersPerThread ActualRows="1000" ActualExecutions="10" /></RunTimeInformation>
            """);
        var node = new PlanGraphNodeUiActionService().CreateNodeFromRelOp(op, op.Name.Namespace, 10, 1000);
        node.HasComparableRows.Should().BeTrue();
        node.ComparableActualRowsDisplay.Should().Be("100");
        node.RowEstimateRatio.Should().Be(1);
        node.ActualRowsDisplay.Should().Be("1,000");
    }

    [Fact]
    public void RowComparison_MissingExecutionCountStaysUnavailable()
    {
        var op = InputFieldMappingTests.RelOp("""
            <RunTimeInformation><RunTimeCountersPerThread ActualRows="0" /></RunTimeInformation>
            """);
        var node = new PlanGraphNodeUiActionService().CreateNodeFromRelOp(op, op.Name.Namespace, 10, 1000);
        node.ActualRowsDisplay.Should().Be("0");
        node.HasComparableRows.Should().BeFalse();
        node.ComparableActualRowsDisplay.Should().Be("N/A");
        node.RowEstimateRatio.Should().BeNull();
    }

    [Fact]
    public void RowComparison_FractionalPerExecutionRowsDoNotDisplayAsZero()
    {
        var node = new PlanNodeViewModel { HasActualRows = true, EstRowsNum = 0.001, ActualRowsNum = 0.0001 };
        node.EstimatedRowsDisplay.Should().Be("0.001");
        node.ComparableActualRowsDisplay.Should().Be("0.0001");
        node.RowEstimateRatioDisplay.Should().Be("0.1×");
    }

    [Fact]
    public void TooltipSummaries_DoNotRepeatWholeRuleLogs()
    {
        var node = new PlanNodeViewModel
        {
            DiagnosticStatusText = "规则执行：Skipped 1000。\n" + string.Join("\n", Enumerable.Repeat("Skipped 记录", 1000)),
            Warnings = new string('x', 500) + "\n其他详情"
        };
        node.DiagnosticStatusSummary.Should().Be("规则执行：Skipped 1000。");
        node.TopDiagnosticSummary.Length.Should().BeLessThanOrEqualTo(180);
        node.TopDiagnosticSummary.Should().NotContain("其他详情");
    }
}
