using System;
using System.Collections.Generic;
using System.Windows;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphViewportUiActionService
    {
        internal const double DefaultMinimumZoom = 0.02;
        internal const double MinimumFitZoom = 0.000001;

        public static double CalculateFitZoom(Rect bounds, Size viewport)
        {
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0
                || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)
                || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height))
                return 1;
            // Keep normal 16-DIP margins, but reserve at most half of a tiny
            // viewport for padding so its available drawing area stays positive.
            double width = Math.Max(viewport.Width - 32, viewport.Width / 2);
            double height = Math.Max(viewport.Height - 32, viewport.Height / 2);
            double zoom = Math.Min(width / bounds.Width, height / bounds.Height);
            return double.IsFinite(zoom) ? Math.Clamp(zoom, MinimumFitZoom, 1.0) : 1;
        }

        public static Point ViewportCenter(Point location, Size viewport, double zoom)
            => new(location.X + viewport.Width / (2 * zoom), location.Y + viewport.Height / (2 * zoom));

        public static Point CenteredLocation(Point center, Size viewport, double zoom)
            => new(center.X - viewport.Width / (2 * zoom), center.Y - viewport.Height / (2 * zoom));
        public void ResetView(
            Action<double> setViewportZoom,
            IReadOnlyList<PlanNodeViewModel> nodes)
        {
            ArgumentNullException.ThrowIfNull(setViewportZoom);
            ArgumentNullException.ThrowIfNull(nodes);

            setViewportZoom(1.0);

            if (nodes.Count == 0)
            {
                return;
            }

        }
    }
}
