using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockCanvasInteractionBinder
    {
        private readonly Core.Services.DeadlockCanvasInteractionService _interactionService;
        private Point _lastPanPoint;
        private bool _isPanning;

        public DeadlockCanvasInteractionBinder(
            Core.Services.DeadlockCanvasInteractionService interactionService)
        {
            _interactionService = interactionService
                ?? throw new ArgumentNullException(nameof(interactionService));
        }

        public void Attach(
            Canvas canvas,
            FrameworkElement panSurface,
            ScaleTransform scaleTransform,
            TranslateTransform translateTransform)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(panSurface);
            ArgumentNullException.ThrowIfNull(scaleTransform);
            ArgumentNullException.ThrowIfNull(translateTransform);

            canvas.MouseWheel += (_, e) =>
            {
                Point mousePosition = e.GetPosition(canvas);
                Core.Services.DeadlockCanvasTransformState? transform =
                    _interactionService.ZoomAt(
                        e.Delta,
                        mousePosition,
                        scaleTransform.ScaleX,
                        translateTransform.X,
                        translateTransform.Y);

                if (transform == null)
                {
                    return;
                }

                scaleTransform.ScaleX = transform.Scale;
                scaleTransform.ScaleY = transform.Scale;
                translateTransform.X = transform.TranslateX;
                translateTransform.Y = transform.TranslateY;
                e.Handled = true;
            };

            canvas.MouseDown += (_, e) =>
            {
                if (e.MiddleButton == MouseButtonState.Pressed
                    || e.LeftButton == MouseButtonState.Pressed)
                {
                    _isPanning = true;
                    _lastPanPoint = e.GetPosition(panSurface);
                    canvas.CaptureMouse();
                    e.Handled = true;
                }
            };

            canvas.MouseMove += (_, e) =>
            {
                if (!_isPanning)
                {
                    return;
                }

                Point current = e.GetPosition(panSurface);
                Core.Services.DeadlockCanvasTransformState transform =
                    _interactionService.Pan(
                        scaleTransform.ScaleX,
                        translateTransform.X,
                        translateTransform.Y,
                        _lastPanPoint,
                        current);

                translateTransform.X = transform.TranslateX;
                translateTransform.Y = transform.TranslateY;
                _lastPanPoint = current;
                e.Handled = true;
            };

            canvas.MouseUp += (_, e) =>
            {
                if (!_isPanning)
                {
                    return;
                }

                _isPanning = false;
                canvas.ReleaseMouseCapture();
                e.Handled = true;
            };
        }
    }
}
