using System;
using System.Windows;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphPanUiActionService
    {
        private readonly Core.Services.PlanGraphPanInteractionService _panInteractionService = new();

        public Core.Services.PlanGraphPanState BeginPan(
            object? originalSource,
            Point pointerPosition,
            Action captureMouse,
            Core.Services.PlanGraphPanState currentState)
        {
            ArgumentNullException.ThrowIfNull(captureMouse);

            if (IsGraphItem(originalSource))
            {
                return currentState;
            }

            captureMouse();
            return _panInteractionService.Begin(pointerPosition);
        }

        public Core.Services.PlanGraphPanState Pan(
            Core.Services.PlanGraphPanState state,
            Point currentPointerPosition,
            Point currentViewportLocation,
            double viewportZoom,
            Action<Point> setViewportLocation)
        {
            ArgumentNullException.ThrowIfNull(setViewportLocation);

            Core.Services.PlanGraphPanUpdate? update =
                _panInteractionService.Pan(
                    state,
                    currentPointerPosition,
                    currentViewportLocation,
                    viewportZoom);

            if (update == null)
            {
                return state;
            }

            setViewportLocation(update.ViewportLocation);
            return update.State;
        }

        public Core.Services.PlanGraphPanState EndPan(
            Core.Services.PlanGraphPanState state,
            Action releaseMouseCapture)
        {
            ArgumentNullException.ThrowIfNull(releaseMouseCapture);

            if (!state.IsPanning)
            {
                return state;
            }

            releaseMouseCapture();
            return _panInteractionService.End(state);
        }

        private static bool IsGraphItem(object? originalSource)
        {
            return originalSource is FrameworkElement
            {
                DataContext: { } dataContext
            }
                && IsGraphItemDataContext(dataContext);
        }

        internal static bool IsGraphItemDataContext(object? dataContext)
        {
            return dataContext is PlanNodeViewModel or ConnectionViewModel;
        }
    }
}
