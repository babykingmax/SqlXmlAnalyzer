using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockGraphPlaybackVisualService
    {
        public Border CreateStepBadge()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 136, 229)),
                CornerRadius = new CornerRadius(8),
                Width = 16,
                Height = 16,
                Child = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        public void ApplyStepBadgePlacement(
            Border badge,
            Core.Services.DeadlockStepBadgePlacement placement)
        {
            if (badge.Child is TextBlock textBlock)
            {
                textBlock.Text = placement.Text;
            }

            Canvas.SetLeft(badge, placement.Left);
            Canvas.SetTop(badge, placement.Top);
        }

        public void ApplyNodeVisualState(
            FrameworkElement element,
            Core.Services.DeadlockGraphNodeVisualState visualState)
        {
            element.Visibility = visualState.IsVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            element.Opacity = visualState.Opacity;

            if (element is Border border)
            {
                if (visualState.IsVictim)
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                    border.BorderThickness = new Thickness(3);
                    border.Background = visualState.IsVictimRevealed
                        ? new SolidColorBrush(Color.FromArgb(50, 211, 47, 47))
                        : Brushes.White;
                }
                else if (visualState.UseDefaultChrome)
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(176, 190, 197));
                    border.BorderThickness = new Thickness(1.5);
                    border.Background = Brushes.White;
                }
            }
        }

        public void ApplyEdgeVisualState(
            DeadlockGraphEdgeElements visuals,
            Core.Services.DeadlockGraphEdgeVisualState visualState)
        {
            Visibility visibility = visualState.IsVisible
                ? Visibility.Visible
                : Visibility.Collapsed;

            visuals.Line.Visibility = visibility;
            visuals.Line.Opacity = visualState.Opacity;
            visuals.Line.StrokeDashArray = CreateStrokeDashArray(visualState.DashPattern);
            visuals.ArrowHead.Visibility = visibility;
            visuals.ArrowHead.Opacity = visualState.Opacity;
            visuals.Label.Visibility = visibility;
            visuals.Label.Opacity = visualState.Opacity;
        }

        private static DoubleCollection? CreateStrokeDashArray(
            Core.Services.DeadlockGraphDashPattern dashPattern)
        {
            return dashPattern switch
            {
                Core.Services.DeadlockGraphDashPattern.Owner => new DoubleCollection { 4, 3 },
                Core.Services.DeadlockGraphDashPattern.Preview => new DoubleCollection { 2, 2 },
                _ => null
            };
        }
    }
}
