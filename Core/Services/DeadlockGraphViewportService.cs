using System;
using System.Collections.Generic;
using System.Windows;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockViewportState(
        double Scale,
        double TranslateX,
        double TranslateY);

    public sealed class DeadlockGraphViewportService
    {
        private const double ProcessWidth = 220;
        private const double ProcessHeight = 90;
        private const double ResourceWidth = 160;
        private const double ResourceHeight = 50;
        private const double Margin = 60;
        private const double DefaultViewWidth = 800;
        private const double DefaultViewHeight = 600;
        private const double MinScale = 0.2;
        private const double MaxScale = 2.0;

        public DeadlockViewportState? CalculateZoomToFit(
            IReadOnlyDictionary<string, Point> nodePositions,
            double viewWidth,
            double viewHeight)
        {
            ArgumentNullException.ThrowIfNull(nodePositions);

            if (nodePositions.Count == 0)
            {
                return null;
            }

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (KeyValuePair<string, Point> node in nodePositions)
            {
                Point position = node.Value;
                bool isResource = node.Key.StartsWith("res_", StringComparison.Ordinal);
                double width = isResource ? ResourceWidth : ProcessWidth;
                double height = isResource ? ResourceHeight : ProcessHeight;

                minX = Math.Min(minX, position.X);
                maxX = Math.Max(maxX, position.X + width);
                minY = Math.Min(minY, position.Y);
                maxY = Math.Max(maxY, position.Y + height);
            }

            double effectiveViewWidth = viewWidth > 0 ? viewWidth : DefaultViewWidth;
            double effectiveViewHeight = viewHeight > 0 ? viewHeight : DefaultViewHeight;
            double contentWidth = (maxX - minX) + Margin * 2;
            double contentHeight = (maxY - minY) + Margin * 2;

            double scale = Math.Min(
                effectiveViewWidth / contentWidth,
                effectiveViewHeight / contentHeight);
            scale = Math.Clamp(scale, MinScale, MaxScale);

            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;
            return new DeadlockViewportState(
                scale,
                effectiveViewWidth / 2 - centerX * scale,
                effectiveViewHeight / 2 - centerY * scale);
        }
    }
}
