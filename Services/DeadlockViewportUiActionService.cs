using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockViewportUiActionService
    {
        private readonly Core.Services.DeadlockGraphViewportService _viewportService;
        private readonly FrameworkElement _viewportElement;
        private readonly ScaleTransform _scaleTransform;
        private readonly TranslateTransform _translateTransform;
        private readonly IReadOnlyDictionary<string, Point> _nodePositions;

        public DeadlockViewportUiActionService(
            Core.Services.DeadlockGraphViewportService viewportService,
            FrameworkElement viewportElement,
            ScaleTransform scaleTransform,
            TranslateTransform translateTransform,
            IReadOnlyDictionary<string, Point> nodePositions)
        {
            _viewportService = viewportService
                ?? throw new ArgumentNullException(nameof(viewportService));
            _viewportElement = viewportElement
                ?? throw new ArgumentNullException(nameof(viewportElement));
            _scaleTransform = scaleTransform
                ?? throw new ArgumentNullException(nameof(scaleTransform));
            _translateTransform = translateTransform
                ?? throw new ArgumentNullException(nameof(translateTransform));
            _nodePositions = nodePositions
                ?? throw new ArgumentNullException(nameof(nodePositions));
        }

        public void ZoomToFit()
        {
            Core.Services.DeadlockViewportState? viewport =
                _viewportService.CalculateZoomToFit(
                    _nodePositions,
                    _viewportElement.ActualWidth,
                    _viewportElement.ActualHeight);

            if (viewport == null)
            {
                return;
            }

            _scaleTransform.ScaleX = viewport.Scale;
            _scaleTransform.ScaleY = viewport.Scale;
            _translateTransform.X = viewport.TranslateX;
            _translateTransform.Y = viewport.TranslateY;
        }
    }
}
