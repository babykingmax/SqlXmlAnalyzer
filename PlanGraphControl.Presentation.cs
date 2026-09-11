using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer;

public partial class PlanGraphControl
{
    private bool _presentationReady;
    private bool _applyingPreferences;
    private bool _hasExplicitViewMode;
    private bool _showConnectionLabels = true;
    private IReadOnlyList<PlanNodeViewModel> _searchMatches = Array.Empty<PlanNodeViewModel>();
    private int _searchIndex = -1;
    private DependencyPropertyDescriptor? _zoomDescriptor;
    private EventHandler? _zoomChanged;
    private bool _observesZoom;
    private bool _fitPending;
    private bool _pageIsCommitting;
    private bool _keepViewportOnPageCommit;
    private bool _viewportUpdateScheduled;
    private long _viewportIntentRevision;
    private PlanNodeViewModel? _pendingViewportNode;
    public event EventHandler? PreferencesChanged;
    public bool ShowConnectionLabels
    {
        get => _showConnectionLabels;
        set
        {
            if (_showConnectionLabels == value) return;
            _showConnectionLabels = value;
            OnPropertyChanged(nameof(ShowConnectionLabels)); OnPropertyChanged(nameof(ConnectionLabelsVisible));
            NotifyPreferencesChanged();
        }
    }
    public bool ConnectionLabelsVisible => ShowConnectionLabels && Editor?.ViewportZoom >= 0.65;
    public bool NodeMetricsVisible => Editor?.ViewportZoom >= 0.55;
    public string ZoomDescription
    {
        get
        {
            double zoom = Editor?.ViewportZoom ?? 1;
            return zoom < 0.01 ? $"{zoom * 100:0.####}%" : $"{zoom:P0}";
        }
    }
    public string GraphLegend => CmbViewMode?.SelectedIndex == (int)DiagramViewMode.Rows
        ? "行数按每次执行比较；倍数 = 实际 ÷ 估算。问题徽标独立显示。"
        : "蓝色深浅表示估算成本；问题徽标独立显示。";
    public string HiddenNodesDescription => $"已折叠 {_masterNodes.Count(n => !n.IsVisible)} 个算子";

    private void InitializePresentation()
    {
        _presentationReady = true;
        _zoomDescriptor = DependencyPropertyDescriptor.FromProperty(Nodify.NodifyEditor.ViewportZoomProperty, typeof(Nodify.NodifyEditor));
        _zoomChanged = (_, _) =>
        {
            OnPropertyChanged(nameof(ConnectionLabelsVisible)); OnPropertyChanged(nameof(ZoomDescription));
            OnPropertyChanged(nameof(NodeMetricsVisible));
        };
        ObserveZoom();
        Loaded += (_, _) => { ObserveZoom(); SchedulePendingViewport(); };
        Editor.SizeChanged += (_, _) => SchedulePendingViewport();
        Editor.UserViewportChanged += (_, _) => PreserveUserViewport();
        Unloaded += (_, _) =>
        {
            if (_observesZoom && _zoomChanged != null) _zoomDescriptor?.RemoveValueChanged(Editor, _zoomChanged);
            _observesZoom = false;
        };
    }
    private void ObserveZoom()
    {
        if (_observesZoom || _zoomDescriptor == null || _zoomChanged == null) return;
        _zoomDescriptor.AddValueChanged(Editor, _zoomChanged);
        _observesZoom = true;
    }
    private void NotifyPreferencesChanged()
    {
        if (_presentationReady && !_applyingPreferences) PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }
    public PlanGraphPreferences CapturePreferences() => new()
    {
        ViewMode = (DiagramViewMode)Math.Max(0, CmbViewMode.SelectedIndex), HasExplicitViewMode = _hasExplicitViewMode,
        LayoutMode = LayoutMode, ColorMode = ColorMode, LinkMetric = LinkMetric, ShowConnectionLabels = ShowConnectionLabels
    };
    public void RestorePreferences(PlanGraphPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _applyingPreferences = true;
        try
        {
            _hasExplicitViewMode = preferences.HasExplicitViewMode;
            CmbViewMode.SelectedIndex = Enum.IsDefined(preferences.ViewMode) ? (int)preferences.ViewMode : 0;
            CmbLayoutMode.SelectedIndex = Enum.IsDefined(preferences.LayoutMode) ? (int)preferences.LayoutMode : 0;
            CmbColorMode.SelectedIndex = Enum.IsDefined(preferences.ColorMode) ? (int)preferences.ColorMode : 0;
            CmbLinkMetric.SelectedIndex = Enum.IsDefined(preferences.LinkMetric) ? (int)preferences.LinkMetric : 0;
            ShowConnectionLabels = preferences.ShowConnectionLabels;
        }
        finally { _applyingPreferences = false; }
    }
    private void ApplyAutomaticViewMode()
    {
        if (_hasExplicitViewMode || _masterNodes.Count == 0) return;
        _applyingPreferences = true;
        try { CmbViewMode.SelectedIndex = _masterNodes.Any(n => n.HasComparableRows) ? (int)DiagramViewMode.Rows : (int)DiagramViewMode.CostPercent; }
        finally { _applyingPreferences = false; }
        ModeUiActionService.ApplyViewMode(CmbViewMode.SelectedIndex, _masterNodes);
    }
    public void FitCurrentGraph()
    {
        _viewportIntentRevision++;
        _fitPending = true;
        _pendingViewportNode = null;
        _keepViewportOnPageCommit = false;
        ApplyPendingViewport();
    }

    private void SchedulePendingViewport()
    {
        if (_viewportUpdateScheduled || (!_fitPending && _pendingViewportNode == null)) return;
        _viewportUpdateScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _viewportUpdateScheduled = false;
            ApplyPendingViewport();
        }));
    }

    private void ApplyPendingViewport()
    {
        // The UI can receive a fit click before its first arrange, or while a
        // page is still being committed in dispatcher batches. Retain that
        // intention until the complete page and its viewport are measurable.
        if (_pageIsCommitting) return;
        Editor.UpdateLayout();
        if (Editor.ActualWidth <= 0 || Editor.ActualHeight <= 0) return;
        if (_fitPending)
        {
            var bounds = CurrentGraphBounds();
            if (bounds.IsEmpty) return;
            double zoom = PlanGraphViewportUiActionService.CalculateFitZoom(bounds, new Size(Editor.ActualWidth, Editor.ActualHeight));
            ApplyViewport(zoom, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), fitCurrentGraph: true);
            _fitPending = false;
        }
        else if (_pendingViewportNode is { } node && Nodes.Contains(node) && node.IsVisible)
        {
            var bounds = NodeBounds(node);
            ApplyViewport(Editor.ViewportZoom, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
            _pendingViewportNode = null;
        }
    }

    private Rect CurrentGraphBounds()
    {
        var bounds = Rect.Empty;
        foreach (var node in Nodes.Where(n => n.IsVisible)) bounds.Union(NodeBounds(node));
        return bounds;
    }

    private Rect NodeBounds(PlanNodeViewModel node)
    {
        var container = Editor.ItemContainerGenerator.ContainerFromItem(node) as FrameworkElement;
        double width = container?.ActualWidth > 0 ? container.ActualWidth : PlanGraphNodeMetrics.Width;
        double height = container?.ActualHeight > 0 ? container.ActualHeight : PlanGraphNodeMetrics.Height;
        return new Rect(node.Location, new Size(width, height));
    }

    private void ApplyViewport(double zoom, Point center, bool fitCurrentGraph = false)
    {
        // Nodify updates ScaleTransform when ViewportZoom changes, but its
        // translation is only refreshed by ViewportLocation. Set both from
        // the same graph-space center instead of zooming around the origin.
        Editor.SetViewport(zoom, center, fitCurrentGraph);
    }

    private void CenterNode(PlanNodeViewModel node)
    {
        _viewportIntentRevision++;
        _fitPending = false;
        _pendingViewportNode = node;
        _keepViewportOnPageCommit = false;
        ApplyPendingViewport();
    }

    private void CompletePageViewport(PlanNodeViewModel? anchor)
    {
        _pageIsCommitting = false;
        if (!_fitPending && _pendingViewportNode == null && !_keepViewportOnPageCommit)
            _pendingViewportNode = anchor;
        ApplyPendingViewport();
    }

    public void ResetZoom() => ChangeZoom(1);
    public void ZoomIn() => ChangeZoom(Editor.ViewportZoom * 1.2);
    public void ZoomOut() => ChangeZoom(Editor.ViewportZoom / 1.2);

    private void ChangeZoom(double zoom)
    {
        PreserveUserViewport();
        Editor.UpdateLayout();
        var center = PlanGraphViewportUiActionService.ViewportCenter(Editor.ViewportLocation,
            new Size(Editor.ActualWidth, Editor.ActualHeight), Editor.ViewportZoom);
        ApplyViewport(zoom, center);
    }

    private void PreserveUserViewport()
    {
        _viewportIntentRevision++;
        _fitPending = false;
        _pendingViewportNode = null;
        _keepViewportOnPageCommit = true;
    }
    public void LocateSelectedNode()
    {
        if (SelectedNode is { } node) FocusNode(node);
    }
    public IReadOnlyList<PlanNodeViewModel> SearchNodes(string query) => PlanGraphPresentationService.Search(_masterNodes, query);
    private void FindNode_Click(object sender, RoutedEventArgs e) => FindNextNode();
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FindNextNode(); e.Handled = true; }
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { _searchIndex = -1; }
    private void FindNextNode()
    {
        _searchMatches = SearchNodes(GraphSearch.Text);
        if (_searchMatches.Count == 0) { SearchResultText.Text = "无匹配算子"; _searchIndex = -1; return; }
        _searchIndex = (_searchIndex + 1) % _searchMatches.Count;
        FocusNode(_searchMatches[_searchIndex]);
        SearchResultText.Text = $"{_searchIndex + 1} / {_searchMatches.Count}";
    }
    private void FitGraph_Click(object sender, RoutedEventArgs e) => FitCurrentGraph();
    private void ResetZoom_Click(object sender, RoutedEventArgs e) => ResetZoom();
    private void LocateSelected_Click(object sender, RoutedEventArgs e) => LocateSelectedNode();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();
    private void DisplaySettings_Click(object sender, RoutedEventArgs e) => DisplaySettingsPopup.IsOpen = !DisplaySettingsPopup.IsOpen;
    private void CrossPageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CrossPageSelector.SelectedItem is not PlanGraphBoundary boundary) return;
        CrossPageSelector.SelectedIndex = -1;
        FocusNode(boundary.Destination);
    }
    private void RefreshPageContext(IReadOnlyCollection<PlanNodeViewModel> page)
    {
        CrossPageSelector.ItemsSource = PlanGraphPresentationService.Boundaries(_masterConnections, page.Where(n => n.IsVisible).ToArray());
        CrossPageSelector.Visibility = CrossPageSelector.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageDescription = PlanGraphPageService.ForIndex(_masterNodes.Count(n => n.IsVisible), _pageStart).Description;
        OnPropertyChanged(nameof(PageDescription)); OnPropertyChanged(nameof(CanPreviousPage)); OnPropertyChanged(nameof(CanNextPage));
        OnPropertyChanged(nameof(HiddenNodesDescription));
    }
}
