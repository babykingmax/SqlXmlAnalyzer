using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PlanComparisonTreeViewRenderer
    {
        public TreeViewItem Render(PlanComparisonTreeNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 2, 0, 2)
            };

            var operatorText = new TextBlock
            {
                Text = node.OperatorText,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480
            };
            var costText = new TextBlock
            {
                Text = node.CostText,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480
            };
            costText.SetResourceReference(TextBlock.ForegroundProperty, node.CostTrend switch
            {
                PlanComparisonCostTrend.Higher => "CriticalBrush",
                PlanComparisonCostTrend.Lower => "GoodBrush",
                _ => "SecondaryTextBrush"
            });
            operatorText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var border = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 4, 0)
            };

            ApplyStateStyle(node, border, operatorText);

            stackPanel.Children.Add(operatorText);
            stackPanel.Children.Add(costText);

            if (node.RuntimeDeltaTexts.Count > 0)
            {
                var runtimeText = new TextBlock
                {
                    Text = " | " + string.Join(", ", node.RuntimeDeltaTexts),
                    FontWeight = FontWeights.Medium,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 480,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                runtimeText.SetResourceReference(TextBlock.ForegroundProperty, "InfoBrush");
                stackPanel.Children.Add(runtimeText);
            }

            border.Child = stackPanel;

            var item = new TreeViewItem
            {
                Header = border,
                Tag = node.Source,
                ToolTip = string.IsNullOrEmpty(node.EvidenceText) ? null : new TextBlock
                    { Text = node.EvidenceText, TextWrapping = TextWrapping.Wrap, MaxWidth = 650 },
                IsExpanded = true
            };

            foreach (PlanComparisonTreeNode child in node.Children)
            {
                item.Items.Add(Render(child));
            }

            return item;
        }

        private static void ApplyStateStyle(
            PlanComparisonTreeNode node,
            Border border,
            TextBlock operatorText)
        {
            switch (node.State)
            {
                case PlanComparisonNodeState.Added:
                case PlanComparisonNodeState.Removed:
                    border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                    border.SetResourceReference(Border.BorderBrushProperty, node.IsPlanB ? "GoodBrush" : "CriticalBrush");
                    border.BorderThickness = new Thickness(1);
                    operatorText.SetResourceReference(TextBlock.ForegroundProperty, node.IsPlanB ? "GoodBrush" : "CriticalBrush");
                    break;
                case PlanComparisonNodeState.OperatorChanged:
                    border.SetResourceReference(Border.BackgroundProperty, "WarningSurfaceBrush");
                    border.SetResourceReference(Border.BorderBrushProperty, "WarningBrush");
                    border.BorderThickness = new Thickness(1);
                    operatorText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                    break;
            }
        }

    }
}
