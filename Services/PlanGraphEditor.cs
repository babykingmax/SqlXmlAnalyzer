using System;
using System.Windows;
using System.Windows.Input;
using Nodify;

namespace SqlXmlAnalyzer;

/// <summary>Allows a bounded plan page to fit in small desktop viewports.</summary>
public sealed class PlanGraphEditor : NodifyEditor
{
    internal event EventHandler? UserViewportChanged;

    static PlanGraphEditor()
    {
        // Nodify 6 coerces the minimum to 10%, even when XAML asks for 2%.
        // A tall 64-node page needs the lower range to fit without clipping.
        MinViewportZoomProperty.OverrideMetadata(typeof(PlanGraphEditor),
            new FrameworkPropertyMetadata(Services.PlanGraphViewportUiActionService.DefaultMinimumZoom, null, (_, value) =>
                double.IsFinite((double)value)
                    ? Math.Max(Services.PlanGraphViewportUiActionService.MinimumFitZoom, (double)value)
                    : Services.PlanGraphViewportUiActionService.DefaultMinimumZoom));
    }

    internal void SetViewport(double zoom, Point center, bool fitCurrentGraph = false)
    {
        if (!double.IsFinite(zoom) || zoom <= 0) zoom = 1;
        // BringIntoView disables interaction synchronously, before WPF has
        // ticked its animation clock and reports IsAnimated on the property.
        bool nodifyPanPending = IsPanning && DisablePanning && DisableZooming;
        bool animatedPan = nodifyPanPending || DependencyPropertyHelper.GetValueSource(this, ViewportLocationProperty).IsAnimated;
        if (animatedPan)
        {
            BeginAnimation(ViewportLocationProperty, null);
            // Nodify's animated BringIntoView disables these until completion.
            // An explicit toolbar action supersedes that animation immediately.
            if (nodifyPanPending)
            {
                IsPanning = false;
                DisablePanning = false;
                DisableZooming = false;
            }
        }
        if (DependencyPropertyHelper.GetValueSource(this, ViewportZoomProperty).IsAnimated)
            BeginAnimation(ViewportZoomProperty, null);
        if (fitCurrentGraph)
        {
            // Only Fit opens the extra range required by this page. Ordinary
            // zoom-out stops at that fitted scale, rather than tending to zero.
            MinViewportZoom = Math.Clamp(zoom, Services.PlanGraphViewportUiActionService.MinimumFitZoom,
                Services.PlanGraphViewportUiActionService.DefaultMinimumZoom);
        }
        else if (zoom >= Services.PlanGraphViewportUiActionService.DefaultMinimumZoom)
            MinViewportZoom = Services.PlanGraphViewportUiActionService.DefaultMinimumZoom;
        ViewportZoom = Math.Clamp(zoom, MinViewportZoom, MaxViewportZoom);
        ViewportLocation = Services.PlanGraphViewportUiActionService.CenteredLocation(center,
            new Size(ActualWidth, ActualHeight), ViewportZoom);
        // A zoom at the origin can leave the location unchanged. In that case
        // Nodify does not raise its location callback, so refresh explicitly.
        TranslateTransform.X = -ViewportLocation.X * ViewportZoom;
        TranslateTransform.Y = -ViewportLocation.Y * ViewportZoom;
    }

    internal void PanViewport(Point location)
    {
        if (location == ViewportLocation) return;
        SetViewport(ViewportZoom, Services.PlanGraphViewportUiActionService.ViewportCenter(location,
            new Size(ActualWidth, ActualHeight), ViewportZoom));
        UserViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double previousZoom = ViewportZoom;
        Point previousLocation = ViewportLocation;
        bool zoomRequested = !e.Handled && e.Delta != 0 && !DisableZooming
            && EditorGestures.Mappings.Editor.ZoomModifierKey == Keyboard.Modifiers;
        base.OnMouseWheel(e);
        if (ViewportZoom >= Services.PlanGraphViewportUiActionService.DefaultMinimumZoom)
            MinViewportZoom = Services.PlanGraphViewportUiActionService.DefaultMinimumZoom;
        // A valid wheel gesture is still the latest intention at a zoom
        // limit; a pending fit must not silently undo that requested limit.
        if (zoomRequested || ViewportZoom != previousZoom || ViewportLocation != previousLocation)
            UserViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point previousLocation = ViewportLocation;
        base.OnMouseMove(e);
        // Includes Nodify's native pan gestures. Mere hover and node dragging
        // leave the viewport untouched and must not cancel a queued fit.
        if (ViewportLocation != previousLocation)
            UserViewportChanged?.Invoke(this, EventArgs.Empty);
    }
}
