using System;
using System.Windows;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphPanState(
        bool IsPanning,
        Point LastPointerPosition);

    public sealed record PlanGraphPanUpdate(
        PlanGraphPanState State,
        Point ViewportLocation);

    public sealed class PlanGraphPanInteractionService
    {
        public PlanGraphPanState Begin(Point pointerPosition)
        {
            return new PlanGraphPanState(
                IsPanning: true,
                pointerPosition);
        }

        public PlanGraphPanUpdate? Pan(
            PlanGraphPanState state,
            Point currentPointerPosition,
            Point currentViewportLocation,
            double viewportZoom)
        {
            if (!state.IsPanning)
            {
                return null;
            }

            if (viewportZoom <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(viewportZoom),
                    "Viewport zoom must be greater than zero.");
            }

            Vector delta = currentPointerPosition - state.LastPointerPosition;
            return new PlanGraphPanUpdate(
                new PlanGraphPanState(
                    IsPanning: true,
                    currentPointerPosition),
                new Point(
                    currentViewportLocation.X - delta.X / viewportZoom,
                    currentViewportLocation.Y - delta.Y / viewportZoom));
        }

        public PlanGraphPanState End(PlanGraphPanState state)
        {
            return new PlanGraphPanState(
                IsPanning: false,
                state.LastPointerPosition);
        }
    }
}
