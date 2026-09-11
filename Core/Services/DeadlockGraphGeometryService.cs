using System;
using System.Collections.Generic;
using System.Windows;
using SqlXmlAnalyzer.Core.Diagnostics;

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
        private readonly IUnexpectedErrorReporter? _unexpectedErrors;

        public DeadlockGraphGeometryService(IUnexpectedErrorReporter? unexpectedErrors = null)
        {
            _unexpectedErrors = unexpectedErrors;
        }

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
            string toId,
            double parallelOffset = 0)
        {
            ArgumentNullException.ThrowIfNull(nodePositions);
            ArgumentNullException.ThrowIfNull(fromId);
            ArgumentNullException.ThrowIfNull(toId);
            try
            {
                return CalculateConnectionPointsCore(nodePositions, fromId, toId, parallelOffset);
            }
            catch (Exception exception)
            {
                ExceptionPolicy.Describe(exception, "DeadlockGraphGeometryService.CalculateConnectionPoints", _unexpectedErrors);
                throw;
            }
        }

        private static DeadlockConnectionPoints CalculateConnectionPointsCore(
            IReadOnlyDictionary<string, Point> nodePositions, string fromId, string toId, double parallelOffset)
        {
            if (!double.IsFinite(parallelOffset)) throw new System.IO.InvalidDataException("DEADLOCK_GEOMETRY_INVALID: 偏移必须为有限数。");

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

            ValidatePoint(fromCenter);
            ValidatePoint(toCenter);
            Vector direction = UnitDirection(toCenter - fromCenter);
            // Keep each shifted origin strictly inside its rectangle. Normal graph offsets
            // are +/-16; this also bounds external callers and avoids missing intersections.
            double limit = Math.Min(Math.Min(fromWidth, fromHeight), Math.Min(toWidth, toHeight)) / 2 - 1;
            double boundedOffset = Math.Clamp(parallelOffset, -limit, limit);
            if (boundedOffset != parallelOffset) Logger.Warning("DeadlockGraphGeometryService: 并行偏移超出节点边界，已限制。");
            Vector offset = new(-direction.Y * boundedOffset, direction.X * boundedOffset);
            var result = new DeadlockConnectionPoints(
                ExitRectangle(fromCenter, fromWidth, fromHeight, offset, direction),
                ExitRectangle(toCenter, toWidth, toHeight, offset, -direction));
            ValidatePoint(result.From);
            ValidatePoint(result.To);
            Logger.Debug("DeadlockGraphGeometryService: 已按偏移直线计算节点边界交点。");
            return result;
        }

        private static Point ExitRectangle(Point center, double width, double height, Vector offset, Vector direction)
        {
            double xDistance = direction.X == 0 ? double.PositiveInfinity
                : (width / 2 - Math.Sign(direction.X) * offset.X) / Math.Abs(direction.X);
            double yDistance = direction.Y == 0 ? double.PositiveInfinity
                : (height / 2 - Math.Sign(direction.Y) * offset.Y) / Math.Abs(direction.Y);
            return center + offset + direction * (Math.Min(xDistance, yDistance) + ConnectionGap);
        }

        private static Vector UnitDirection(Vector direction)
        {
            double scale = Math.Max(Math.Abs(direction.X), Math.Abs(direction.Y));
            if (!double.IsFinite(scale)) throw new System.IO.InvalidDataException("DEADLOCK_GEOMETRY_INVALID: 节点间距离超出范围。");
            if (scale == 0) return new Vector(1, 0); // Deterministic finite fallback for coincident centers.
            direction /= scale;
            return direction / direction.Length;
        }

        private static void ValidatePoint(Point point)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new System.IO.InvalidDataException("DEADLOCK_GEOMETRY_INVALID: 节点坐标必须为有限数。");
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
