using System;
using System.Windows;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockCanvasTransformState(
        double Scale,
        double TranslateX,
        double TranslateY);

    public sealed class DeadlockCanvasInteractionService
    {
        private const double ZoomInFactor = 1.1;
        private const double ZoomOutFactor = 0.9;
        private const double MinScale = 0.1;
        private const double MaxScale = 10.0;

        public DeadlockCanvasTransformState? ZoomAt(
            int wheelDelta,
            Point anchor,
            double currentScale,
            double translateX,
            double translateY)
        {
            if (currentScale <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentScale),
                    "Current scale must be greater than zero.");
            }

            double zoomFactor = wheelDelta > 0 ? ZoomInFactor : ZoomOutFactor;
            double newScale = currentScale * zoomFactor;
            if (newScale < MinScale || newScale > MaxScale)
            {
                return null;
            }

            double absoluteX = (anchor.X - translateX) / currentScale;
            double absoluteY = (anchor.Y - translateY) / currentScale;

            return new DeadlockCanvasTransformState(
                newScale,
                anchor.X - absoluteX * newScale,
                anchor.Y - absoluteY * newScale);
        }

        public DeadlockCanvasTransformState Pan(
            double currentScale,
            double translateX,
            double translateY,
            Point previous,
            Point current)
        {
            return new DeadlockCanvasTransformState(
                currentScale,
                translateX + current.X - previous.X,
                translateY + current.Y - previous.Y);
        }
    }
}
