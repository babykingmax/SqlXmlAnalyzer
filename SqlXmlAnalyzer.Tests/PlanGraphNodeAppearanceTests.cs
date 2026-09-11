using FluentAssertions;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;
using static SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanGraphNodeAppearanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NodeCard_KeepsOneSelectionBorderAndTheCollapseButtonInsideItsLayoutBounds(bool dark) => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        WorkspaceThemeService.Apply(graph.Resources, dark);
        var palette = WorkspaceThemeService.Palette(dark);
        var parent = InputFieldMappingTests.RelOp("<NestedLoops><RelOp NodeId='1' PhysicalOp='Constant Scan' EstimateRows='1'/></NestedLoops>");
        parent.SetAttributeValue("NodeId", "0");
        graph.LoadFromExecutionPlan(parent.Document!, parent.Name.Namespace,
            parent.DescendantsAndSelf().Where(e => e.Name.LocalName == "RelOp").ToArray());
        // Loading selects the most expensive operator; start explicitly with no selection.
        graph.SelectedNode = null;
        graph.Measure(new Size(900, 600)); graph.Arrange(new Rect(0, 0, 900, 600)); graph.UpdateLayout();
        var node = graph.AllNodes.Single(n => n.NodeId == "0");
        var editor = (Nodify.NodifyEditor)graph.FindName("Editor");
        var container = (Nodify.ItemContainer)editor.ItemContainerGenerator.ContainerFromItem(node);
        var visuals = WorkspaceAccessibility.Descendants(container).OfType<FrameworkElement>().ToArray();
        var card = visuals.OfType<Border>().Single(b => b.Name == "NodeCard");
        var button = visuals.OfType<Button>().Single(b => b.Name == "CollapseNodeButton");
        var name = visuals.OfType<TextBlock>().Single(b => b.Name == "OperatorName");
        var icon = visuals.OfType<ShapePath>().Single(b => b.Name == "OperatorIcon");
        var metric = visuals.OfType<TextBlock>().Single(b => b.Name == "NodePrimaryMetric");
        card.ActualWidth.Should().Be(PlanGraphNodeMetrics.Width);
        card.ActualHeight.Should().Be(PlanGraphNodeMetrics.Height);
        container.ActualWidth.Should().Be(card.ActualWidth);
        container.ActualHeight.Should().Be(card.ActualHeight);
        ((SolidColorBrush)card.BorderBrush).Color.Should().Be(palette["BorderBrush"]);
        icon.Width.Should().Be(24);
        name.FontSize.Should().Be(13);
        new Rect(card.RenderSize).Contains(button.TransformToAncestor(card).TransformBounds(new Rect(button.RenderSize))).Should().BeTrue();

        container.IsSelected = true;
        graph.UpdateLayout();
        card.BorderThickness.Should().Be(new Thickness(2));
        ((SolidColorBrush)card.BorderBrush).Color.Should().Be(palette["AccentBrush"]);
        ((SolidColorBrush)card.Background).Color.Should().Be(palette["SurfaceBrush"]);
        ((SolidColorBrush)name.Foreground).Color.Should().Be(palette["TextBrush"]);
        ((SolidColorBrush)metric.Foreground).Color.Should().Be(palette["TextBrush"]);
        container.ActualWidth.Should().Be(PlanGraphNodeMetrics.Width, "selection should not move the connection anchors");
        VisualTreeHelper.GetChild(container, 0).Should().BeOfType<ContentPresenter>("the card owns its border; Nodify must not draw another selection frame");
        return Task.CompletedTask;
    });

    [Fact]
    public void DiagnosticBadge_IncludesSeverityAndCountAtOverviewZoom() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var graph = new PlanGraphControl();
        var document = Plan();
        var report = Engine(
            new NativeRule("TEST_BADGE_A", c => Core.Rules.RuleEvaluation.Hit(Observation(c, code: "card-warning", severity: IssueSeverity.Warning))),
            new NativeRule("TEST_BADGE_B", c => Core.Rules.RuleEvaluation.Hit(Observation(c, code: "card-information", severity: IssueSeverity.Info))))
            .AnalyzePlanDetailed(document, Ns);
        report.Diagnostics.Should().HaveCount(2, "the badge fixture needs two distinct diagnostic meanings");
        graph.LoadFromExecutionPlan(document, Ns, diagnostics: report);
        graph.Measure(new Size(900, 600)); graph.Arrange(new Rect(0, 0, 900, 600)); graph.UpdateLayout();
        var editor = (Nodify.NodifyEditor)graph.FindName("Editor");
        editor.ViewportZoom = 0.4;
        graph.UpdateLayout();
        var elements = WorkspaceAccessibility.Descendants(graph).OfType<FrameworkElement>().ToArray();
        var badge = elements.OfType<Border>().Single(b => b.Name == "DiagnosticBadge");
        ((TextBlock)badge.Child).Text.Should().Be("警告 2");
        // This unit test measures the production visual tree without attaching an HWND.
        // IsVisible also depends on a presentation source, so inspect the complete ancestor
        // visibility chain: a collapsed metrics panel must never hide the sibling badge.
        IsVisibleWithinGraph(badge, graph).Should().BeTrue("problem markers remain useful when ordinary numeric labels are hidden");
        badge.ActualWidth.Should().BeGreaterThan(0);
        badge.ActualHeight.Should().BeGreaterThan(0);
        IsVisibleWithinGraph(elements.OfType<TextBlock>().Single(b => b.Name == "NodePrimaryMetric"), graph).Should().BeFalse();
        elements.OfType<TextBlock>().Single(b => b.Name == "NodeObjectLabel").Visibility.Should().Be(Visibility.Collapsed);
        return Task.CompletedTask;
    });

    private static bool IsVisibleWithinGraph(FrameworkElement element, PlanGraphControl graph)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement visual && visual.Visibility != Visibility.Visible) return false;
            if (ReferenceEquals(current, graph)) return true;
        }
        return false;
    }
}
