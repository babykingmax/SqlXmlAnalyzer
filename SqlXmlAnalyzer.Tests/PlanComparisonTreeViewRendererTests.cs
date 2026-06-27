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

                var border = (Border)item.Header;
                var panel = (StackPanel)border.Child;
                var operatorText = (TextBlock)panel.Children[0];
                var costText = (TextBlock)panel.Children[1];
                var runtimeText = (TextBlock)panel.Children[2];

                border.BorderBrush.Should().BeSameAs(Brushes.Green);
                border.BorderThickness.Left.Should().Be(1);
                operatorText.Foreground.Should().BeSameAs(Brushes.DarkGreen);
                costText.Foreground.Should().BeSameAs(Brushes.Gray);
                runtimeText.Foreground.Should().BeSameAs(Brushes.Purple);
                runtimeText.Text.Should().Be(" | Elapsed: 20 (+12)");
            });
        }

        [Theory]
        [InlineData(PlanComparisonCostTrend.Higher, "Red")]
        [InlineData(PlanComparisonCostTrend.Lower, "Green")]
        [InlineData(PlanComparisonCostTrend.Neutral, "Gray")]
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

                var border = (Border)item.Header;
                var panel = (StackPanel)border.Child;
                var costText = (TextBlock)panel.Children[1];

                costText.Foreground.Should().BeSameAs(GetBrush(expectedBrushName));
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

        private static Brush GetBrush(string brushName)
        {
            return brushName switch
            {
                "Red" => Brushes.Red,
                "Green" => Brushes.Green,
                _ => Brushes.Gray
            };
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
