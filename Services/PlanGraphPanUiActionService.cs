using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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
            // TextBlock inlines raise input from Run (a content element), and
            // button templates may override DataContext. Walk both logical
            // and visual ancestry before capturing the mouse for canvas pan.
            var current = originalSource as DependencyObject;
            while (current != null)
            {
                if (current is Nodify.ItemContainer or ButtonBase) return true;
                if (current is FrameworkElement element && IsGraphItemDataContext(element.DataContext)) return true;
                if (current is FrameworkContentElement content && IsGraphItemDataContext(content.DataContext)) return true;
                current = current switch
                {
                    FrameworkContentElement inline => inline.Parent,
                    Visual visual => VisualTreeHelper.GetParent(visual) ?? LogicalTreeHelper.GetParent(visual),
                    _ => LogicalTreeHelper.GetParent(current)
                };
            }
            return false;
        }

        internal static bool IsGraphItemDataContext(object? dataContext)
        {
            return dataContext is PlanNodeViewModel or ConnectionViewModel;
        }
    }
}
