using System;
using System.Collections.Generic;
using System.Windows;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockConnectionPoints(
        Point From,
        Point To);

    public sealed record DeadlockArrowHeadPoints(
        Point Tip,
        Point Left,
        Point Right);

    public sealed class DeadlockGraphGeometryService
    {
        private const double ProcessWidth = 220;
        private const double ProcessHeight = 90;
        private const double ResourceWidth = 160;
        private const double ResourceHeight = 50;
        private const double ConnectionGap = 3;
        private const double ArrowSize = 10;
        private const double ArrowWidth = 6;

        public DeadlockConnectionPoints CalculateConnectionPoints(
            IReadOnlyDictionary<string, Point> nodePositions,
            string fromId,
            string toId)
        {
            ArgumentNullException.ThrowIfNull(nodePositions);

            (double fromWidth, double fromHeight) = GetNodeSize(fromId);
            (double toWidth, double toHeight) = GetNodeSize(toId);

            Point fromTopLeft = nodePositions.TryGetValue(fromId, out Point fromPosition)
                ? fromPosition
                : new Point(80, 150);
            Point toTopLeft = nodePositions.TryGetValue(toId, out Point toPosition)
                ? toPosition
                : new Point(400, 150);

            Point fromCenter = new(
                fromTopLeft.X + fromWidth / 2,
                fromTopLeft.Y + fromHeight / 2);
            Point toCenter = new(
                toTopLeft.X + toWidth / 2,
                toTopLeft.Y + toHeight / 2);

            Vector direction = toCenter - fromCenter;
            double distance = Math.Max(direction.Length, 0.1);
            double unitX = direction.X / distance;
            double unitY = direction.Y / distance;

            double fromFactor = Math.Min(
                (fromWidth / 2) / Math.Max(0.001, Math.Abs(unitX)),
                (fromHeight / 2) / Math.Max(0.001, Math.Abs(unitY)));
            double toFactor = Math.Min(
                (toWidth / 2) / Math.Max(0.001, Math.Abs(unitX)),
                (toHeight / 2) / Math.Max(0.001, Math.Abs(unitY)));

            return new DeadlockConnectionPoints(
                new Point(
                    fromCenter.X + unitX * (fromFactor + ConnectionGap),
                    fromCenter.Y + unitY * (fromFactor + ConnectionGap)),
                new Point(
                    toCenter.X - unitX * (toFactor + ConnectionGap),
                    toCenter.Y - unitY * (toFactor + ConnectionGap)));
        }

        public DeadlockArrowHeadPoints CalculateArrowHead(
            Point tip,
            Point fromPoint)
        {
            Vector direction = tip - fromPoint;
            double length = Math.Max(direction.Length, 0.1);
            double unitX = direction.X / length;
            double unitY = direction.Y / length;

            return new DeadlockArrowHeadPoints(
                tip,
                new Point(
                    tip.X - unitX * ArrowSize - unitY * ArrowWidth,
                    tip.Y - unitY * ArrowSize + unitX * ArrowWidth),
                new Point(
                    tip.X - unitX * ArrowSize + unitY * ArrowWidth,
                    tip.Y - unitY * ArrowSize - unitX * ArrowWidth));
        }

        private static (double Width, double Height) GetNodeSize(string nodeId)
        {
            return nodeId.StartsWith("res_", StringComparison.Ordinal)
                ? (ResourceWidth, ResourceHeight)
                : (ProcessWidth, ProcessHeight);
        }
    }
}
