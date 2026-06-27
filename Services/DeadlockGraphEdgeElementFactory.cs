using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SqlXmlAnalyzer.Services
{
    internal sealed record DeadlockGraphEdgeElements(
        Line Line,
        Polygon ArrowHead,
        Border Label);

    internal sealed class DeadlockGraphEdgeElementFactory
    {
        private readonly Core.Services.DeadlockGraphGeometryService _geometryService;

        public DeadlockGraphEdgeElementFactory(
            Core.Services.DeadlockGraphGeometryService geometryService)
        {
            _geometryService = geometryService
                ?? throw new ArgumentNullException(nameof(geometryService));
        }

        public DeadlockGraphEdgeElements CreateEdge(
            Core.Services.DeadlockConnectionPoints points,
            string label,
            bool isWaitEdge)
        {
            Brush brush = isWaitEdge
                ? new SolidColorBrush(Color.FromRgb(211, 47, 47))
                : new SolidColorBrush(Color.FromRgb(56, 142, 60));

            var line = new Line
            {
                X1 = points.From.X,
                Y1 = points.From.Y,
                X2 = points.To.X,
                Y2 = points.To.Y,
                Stroke = brush,
                StrokeThickness = isWaitEdge ? 2.5 : 2.0,
                StrokeDashArray = isWaitEdge ? null : new DoubleCollection { 4, 3 }
            };

            Polygon arrowHead = CreateArrowHead(points.To, points.From, brush);
            Border labelElement = CreateLabel(label, brush);
            PlaceLabel(labelElement, points);

            return new DeadlockGraphEdgeElements(line, arrowHead, labelElement);
        }

        public void UpdateEdge(
            DeadlockGraphEdgeElements elements,
            Core.Services.DeadlockConnectionPoints points)
        {
            ArgumentNullException.ThrowIfNull(elements);

            elements.Line.X1 = points.From.X;
            elements.Line.Y1 = points.From.Y;
            elements.Line.X2 = points.To.X;
            elements.Line.Y2 = points.To.Y;

            UpdateArrowHeadPosition(elements.ArrowHead, points.To, points.From);
            PlaceLabel(elements.Label, points);
        }

        private Polygon CreateArrowHead(Point tip, Point fromPoint, Brush fill)
        {
            Core.Services.DeadlockArrowHeadPoints points =
                _geometryService.CalculateArrowHead(tip, fromPoint);

            return new Polygon
            {
                Points = new PointCollection { points.Tip, points.Left, points.Right },
                Fill = fill,
                Stroke = fill,
                StrokeThickness = 0.5
            };
        }

        private void UpdateArrowHeadPosition(
            Polygon arrowHead,
            Point tip,
            Point fromPoint)
        {
            Core.Services.DeadlockArrowHeadPoints points =
                _geometryService.CalculateArrowHead(tip, fromPoint);

            arrowHead.Points[0] = points.Tip;
            arrowHead.Points[1] = points.Left;
            arrowHead.Points[2] = points.Right;
        }

        private static Border CreateLabel(string label, Brush brush)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                BorderBrush = brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = brush
                }
            };
        }

        private static void PlaceLabel(
            Border label,
            Core.Services.DeadlockConnectionPoints points)
        {
            double labelWidth = label.ActualWidth > 0 ? label.ActualWidth : 50;
            double labelHeight = label.ActualHeight > 0 ? label.ActualHeight : 16;

            Canvas.SetLeft(label, (points.From.X + points.To.X) / 2 - labelWidth / 2);
            Canvas.SetTop(label, (points.From.Y + points.To.Y) / 2 - labelHeight / 2);
        }
    }
}
