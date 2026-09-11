using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanComparisonTreeViewRendererTests
    {
        [Fact]
        public void Render_CreatesExpandedTreeItemWithSourceTagAndChildren()
        {
            RunOnStaThread(() =>
            {
                XElement source = new("RelOp");
                var child = CreateNode("Index Seek", new XElement("Child"));
                var node = CreateNode("Nested Loops", source, children: new[] { child });
                var renderer = new PlanComparisonTreeViewRenderer();

                TreeViewItem item = renderer.Render(node);
                WorkspaceThemeService.Apply(item.Resources, false);

                item.Tag.Should().BeSameAs(source);
                item.IsExpanded.Should().BeTrue();
                item.Items.Count.Should().Be(1);
            });
        }

        [Fact]
        public void Render_ForAddedPlanBNode_AppliesAddedStylingAndRuntimeDeltaText()
        {
            RunOnStaThread(() =>
            {
                var node = CreateNode(
                    "Index Seek [Added]",
                    new XElement("RelOp"),
                    state: PlanComparisonNodeState.Added,
                    isPlanB: true,
                    runtimeDeltaTexts: new[] { "Elapsed: 20 (+12)" });
                var renderer = new PlanComparisonTreeViewRenderer();

                TreeViewItem item = renderer.Render(node);
                WorkspaceThemeService.Apply(item.Resources, false);

                var border = (Border)item.Header;
                var panel = (StackPanel)border.Child;
                var operatorText = (TextBlock)panel.Children[0];
                var costText = (TextBlock)panel.Children[1];
                var runtimeText = (TextBlock)panel.Children[2];

                border.BorderBrush.Should().BeSameAs(item.Resources["GoodBrush"]);
                border.BorderThickness.Left.Should().Be(1);
                operatorText.Foreground.Should().BeSameAs(item.Resources["GoodBrush"]);
                costText.Foreground.Should().BeSameAs(item.Resources["SecondaryTextBrush"]);
                runtimeText.Foreground.Should().BeSameAs(item.Resources["InfoBrush"]);
                runtimeText.Text.Should().Be(" | Elapsed: 20 (+12)");
            });
        }

        [Theory]
        [InlineData(PlanComparisonCostTrend.Higher, "CriticalBrush")]
        [InlineData(PlanComparisonCostTrend.Lower, "GoodBrush")]
        [InlineData(PlanComparisonCostTrend.Neutral, "SecondaryTextBrush")]
        public void Render_UsesCostTrendBrush(
            PlanComparisonCostTrend trend,
            string expectedBrushName)
        {
            RunOnStaThread(() =>
            {
                var node = CreateNode(
                    "Sort",
                    new XElement("RelOp"),
                    costTrend: trend);
                var renderer = new PlanComparisonTreeViewRenderer();

                TreeViewItem item = renderer.Render(node);
                WorkspaceThemeService.Apply(item.Resources, false);

                var border = (Border)item.Header;
                var panel = (StackPanel)border.Child;
                var costText = (TextBlock)panel.Children[1];

                costText.Foreground.Should().BeSameAs(item.Resources[expectedBrushName]);
            });
        }

        [Fact]
        public void Render_ExistingNodesFollowThemeChangesWithoutLosingStateText()
        {
            RunOnStaThread(() =>
            {
                var item = new PlanComparisonTreeViewRenderer().Render(CreateNode("Sort [Added]", new XElement("RelOp"),
                    state: PlanComparisonNodeState.Added, isPlanB: true, runtimeDeltaTexts: new[] { "Rows +10" }));
                var panel = (StackPanel)((Border)item.Header).Child;
                foreach (bool dark in new[] { false, true, false })
                {
                    WorkspaceThemeService.Apply(item.Resources, dark);
                    var surface = WorkspaceThemeService.Palette(dark)["SurfaceBrush"];
                    foreach (TextBlock text in panel.Children)
                        WorkspaceInteractionTests.Contrast(((SolidColorBrush)text.Foreground).Color, surface).Should().BeGreaterThanOrEqualTo(4.5);
                    ((TextBlock)panel.Children[0]).Text.Should().Contain("[Added]");
                }
            });
        }

        [Fact]
        public void Render_WhenNodeIsNull_Throws()
        {
            var renderer = new PlanComparisonTreeViewRenderer();

            Action act = () => renderer.Render(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        private static PlanComparisonTreeNode CreateNode(
            string operatorText,
            XElement source,
            PlanComparisonNodeState state = PlanComparisonNodeState.Unchanged,
            PlanComparisonCostTrend costTrend = PlanComparisonCostTrend.Neutral,
            bool isPlanB = false,
            IReadOnlyList<string>? runtimeDeltaTexts = null,
            IReadOnlyList<PlanComparisonTreeNode>? children = null)
        {
            return new PlanComparisonTreeNode(
                source,
                operatorText,
                " (Cost: 1.0000)",
                state,
                costTrend,
                isPlanB,
                runtimeDeltaTexts ?? Array.Empty<string>(),
                children ?? Array.Empty<PlanComparisonTreeNode>());
        }

        private static void RunOnStaThread(Action action)
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception != null)
            {
                throw exception;
            }
        }
    }
}
