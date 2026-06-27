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
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            var operatorText = new TextBlock
            {
                Text = node.OperatorText,
                FontWeight = FontWeights.SemiBold
            };
            var costText = new TextBlock
            {
                Text = node.CostText,
                Foreground = GetCostBrush(node.CostTrend)
            };

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
                    Foreground = node.IsPlanB ? Brushes.Purple : Brushes.Teal,
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                stackPanel.Children.Add(runtimeText);
            }

            border.Child = stackPanel;

            var item = new TreeViewItem
            {
                Header = border,
                Tag = node.Source,
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
                    border.Background = node.IsPlanB
                        ? new SolidColorBrush(Color.FromArgb(40, 76, 175, 80))
                        : new SolidColorBrush(Color.FromArgb(40, 244, 67, 54));
                    border.BorderBrush = node.IsPlanB ? Brushes.Green : Brushes.Red;
                    border.BorderThickness = new Thickness(1);
                    operatorText.Foreground = node.IsPlanB ? Brushes.DarkGreen : Brushes.DarkRed;
                    break;
                case PlanComparisonNodeState.OperatorChanged:
                    border.Background = new SolidColorBrush(Color.FromArgb(40, 255, 152, 0));
                    border.BorderBrush = Brushes.Orange;
                    border.BorderThickness = new Thickness(1);
                    operatorText.Foreground = Brushes.DarkOrange;
                    break;
            }
        }

        private static Brush GetCostBrush(PlanComparisonCostTrend costTrend)
        {
            return costTrend switch
            {
                PlanComparisonCostTrend.Higher => Brushes.Red,
                PlanComparisonCostTrend.Lower => Brushes.Green,
                _ => Brushes.Gray
            };
        }
    }
}
