using System.ComponentModel;
using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Views;

public partial class PlanWorkspaceView
{
    private bool _leftOpen = true;
    private bool _rightOpen = true;
    private double _leftWidth = 280;
    private double _rightWidth = 320;
    private double _auxiliaryHeight = 220;
    private PlanWorkspacePane _activePane = PlanWorkspacePane.Graph;
    private bool _applyingLayout;
    private bool _applyingPreferences;
    private bool _preferencesLoaded;
    private bool _preferencesDirty;
    private PlanGridColumnPreference[] _defaultColumns = [];
    private IEnumerable? _operatorItemsSource;
    private ICollectionView? _operatorCollectionView;
    private SortDescription[] _operatorSort = [new("OwnCost", ListSortDirection.Descending)];
    private bool _applyingOperatorSort;
    private bool? _shortWorkspace;
    private readonly DispatcherTimer _preferencesTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public PlanWorkspacePreferencesStore PreferencesStore { get; set; } = PlanWorkspacePreferencesStore.Default;

    private void InitializePresentation()
    {
        _defaultColumns = CaptureColumns();
        SizeChanged += (_, _) => UpdateLayoutMode();
        Loaded += (_, _) =>
        {
            if (!_preferencesLoaded) ApplyPreferences(PreferencesStore.Load());
            UpdateLayoutMode();
        };
        Unloaded += (_, _) => { _preferencesTimer.Stop(); SavePreferences(); };
        _preferencesTimer.Tick += (_, _) => { _preferencesTimer.Stop(); SavePreferences(); };
        PlanNodifyGraph.PreferencesChanged += (_, _) => QueueSavePreferences();
        RecostGrid.ColumnReordered += (_, _) => QueueSavePreferences();
        foreach (DataGridColumn column in RecostGrid.Columns)
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                ?.AddValueChanged(column, (_, _) => QueueSavePreferences());
        // A new statement replaces AllNodes without reloading the already selected tab.
        // Transfer the actual view sort (including multi-column or cleared user sorts).
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid))
            ?.AddValueChanged(RecostGrid, (_, _) => OperatorItemsSourceChanged());
        OperatorItemsSourceChanged();
    }

    private void OperatorItemsSourceChanged()
    {
        if (_applyingOperatorSort || ReferenceEquals(_operatorItemsSource, RecostGrid.ItemsSource)) return;
        _applyingOperatorSort = true;
        try
        {
            if (_operatorCollectionView != null)
                ((INotifyCollectionChanged)_operatorCollectionView.SortDescriptions).CollectionChanged -= OperatorSortChanged;
            _operatorItemsSource = RecostGrid.ItemsSource;
            _operatorCollectionView = _operatorItemsSource == null ? null : CollectionViewSource.GetDefaultView(_operatorItemsSource);
            if (_operatorCollectionView is not { CanSort: true } current) return;
            ((INotifyCollectionChanged)current.SortDescriptions).CollectionChanged += OperatorSortChanged;
            using (current.DeferRefresh())
            {
                current.SortDescriptions.Clear();
                foreach (var sort in _operatorSort) current.SortDescriptions.Add(sort);
            }
            foreach (var column in RecostGrid.Columns) column.SortDirection = null;
            foreach (var sort in _operatorSort)
            {
                var column = RecostGrid.Columns.FirstOrDefault(column => column.SortMemberPath == sort.PropertyName);
                if (column != null) column.SortDirection = sort.Direction;
            }
        }
        finally { _applyingOperatorSort = false; }
    }

    private void OperatorSortChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_applyingOperatorSort || _operatorCollectionView == null) return;
        if (e.Action != NotifyCollectionChangedAction.Reset)
        {
            _operatorSort = _operatorCollectionView.SortDescriptions.ToArray();
            return;
        }
        // DataGrid clears the OLD collection's sorting while coercing a new ItemsSource,
        // before the dependency-property change notification. Do not treat that as user intent.
        var source = _operatorItemsSource;
        var collection = _operatorCollectionView;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            if (ReferenceEquals(source, RecostGrid.ItemsSource) && ReferenceEquals(collection, _operatorCollectionView))
                _operatorSort = collection.SortDescriptions.ToArray();
        }));
    }

    public PlanWorkspacePreferences CapturePreferences() => new()
    {
        LeftWidth = _leftWidth,
        RightWidth = _rightWidth,
        LeftOpen = _leftOpen,
        RightOpen = _rightOpen,
        AuxiliaryOpen = AuxiliaryPanel.IsExpanded,
        AuxiliaryHeight = _auxiliaryHeight,
        ActiveView = OperatorsTab.IsSelected ? "operators" : "graph",
        CompactPane = _activePane switch { PlanWorkspacePane.Issues => "issues", PlanWorkspacePane.Details => "details", _ => "graph" },
        Columns = CaptureColumns(),
        Graph = PlanNodifyGraph.CapturePreferences()
    };

    public void ApplyPreferences(PlanWorkspacePreferences preferences)
    {
        preferences = PlanWorkspacePreferencesStore.Normalize(preferences);
        _applyingPreferences = true;
        try
        {
            _leftWidth = preferences.LeftWidth;
            _rightWidth = preferences.RightWidth;
            _leftOpen = preferences.LeftOpen;
            _rightOpen = preferences.RightOpen;
            _auxiliaryHeight = preferences.AuxiliaryHeight;
            _activePane = preferences.CompactPane switch
            {
                "issues" => PlanWorkspacePane.Issues,
                "details" => PlanWorkspacePane.Details,
                _ => PlanWorkspacePane.Graph
            };
            AuxiliaryPanel.IsExpanded = preferences.AuxiliaryOpen;
            (preferences.ActiveView == "operators" ? OperatorsTab : GraphTab).IsSelected = true;
            ApplyColumns(preferences.Columns.Count > 0 ? preferences.Columns : _defaultColumns);
            PlanNodifyGraph.RestorePreferences(preferences.Graph);
            _preferencesLoaded = true;
            _preferencesDirty = false;
            UpdateLayoutMode();
        }
        finally { _applyingPreferences = false; }
    }

    private PlanGridColumnPreference[] CaptureColumns() => RecostGrid.Columns.Select((column, ordinal) =>
        new PlanGridColumnPreference(column.Header?.ToString() ?? "", Math.Max(48,
            double.IsFinite(column.ActualWidth) && column.ActualWidth > 0 ? column.ActualWidth :
            column.Width.IsAbsolute ? column.Width.Value : 120), column.DisplayIndex < 0 ? ordinal : column.DisplayIndex,
            column.Visibility == Visibility.Visible)).ToArray();

    private void ApplyColumns(IReadOnlyList<PlanGridColumnPreference> preferences)
    {
        foreach (var preference in preferences.OrderBy(item => item.DisplayIndex))
        {
            var column = RecostGrid.Columns.FirstOrDefault(item => Equals(item.Header?.ToString(), preference.Key));
            if (column == null) continue;
            column.Width = new DataGridLength(preference.Width);
            column.DisplayIndex = Math.Clamp(preference.DisplayIndex, 0, RecostGrid.Columns.Count - 1);
            column.Visibility = preference.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
        if (!RecostGrid.Columns.Any(column => column.Visibility == Visibility.Visible))
            RecostGrid.Columns[0].Visibility = Visibility.Visible;
    }

    private void QueueSavePreferences()
    {
        if (!_preferencesLoaded || _applyingPreferences || _applyingLayout) return;
        _preferencesDirty = true;
        _preferencesTimer.Stop();
        _preferencesTimer.Start();
    }

    public void SavePreferences()
    {
        if (!_preferencesDirty || !_preferencesLoaded || _applyingPreferences) return;
        if (PreferencesStore.Save(CapturePreferences())) _preferencesDirty = false;
    }

    private void UpdateLayoutMode()
    {
        if (_applyingLayout || PlanContentGrid == null || PlanGraphTabControl == null || AuxiliaryContent == null) return;
        _applyingLayout = true;
        try
        {
            UpdateHeightMode();
            var layout = PlanWorkspaceLayoutService.Calculate(ActualWidth, _leftOpen, _rightOpen, _activePane, _leftWidth, _rightWidth);
            LeftPanel.IsExpanded = layout.ShowIssues;
            RightPanel.IsExpanded = layout.ShowDetails;
            LeftPanel.Visibility = layout.ShowIssues ? Visibility.Visible : Visibility.Collapsed;
            RightPanel.Visibility = layout.ShowDetails ? Visibility.Visible : Visibility.Collapsed;
            PlanGraphTabControl.Visibility = layout.ShowGraph ? Visibility.Visible : Visibility.Collapsed;
            bool narrow = layout.Mode == PlanWorkspaceLayoutMode.Narrow;
            PlanContentGrid.ColumnDefinitions[0].Width = narrow && layout.ShowIssues ? new GridLength(1, GridUnitType.Star) : new GridLength(layout.LeftWidth);
            PlanContentGrid.ColumnDefinitions[1].Width = new GridLength(layout.LeftSplitterWidth);
            PlanContentGrid.ColumnDefinitions[2].Width = layout.ShowGraph ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            PlanContentGrid.ColumnDefinitions[3].Width = new GridLength(layout.RightSplitterWidth);
            PlanContentGrid.ColumnDefinitions[4].Width = narrow && layout.ShowDetails ? new GridLength(1, GridUnitType.Star) : new GridLength(layout.RightWidth);
            LeftSplitter.Visibility = layout.LeftSplitterWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
            RightSplitter.Visibility = layout.RightSplitterWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
            AuxiliaryContent.Height = PlanWorkspaceLayoutService.AuxiliaryHeight(_auxiliaryHeight, ActualHeight);
        }
        finally { _applyingLayout = false; }
    }

    private void UpdateHeightMode()
    {
        bool shortWorkspace = ActualHeight > 0 && ActualHeight < 420;
        if (_shortWorkspace == shortWorkspace) return;
        _shortWorkspace = shortWorkspace;
        WorkspaceRoot.Margin = new Thickness(shortWorkspace ? 2 : 4);
        WorkspaceHeader.Padding = shortWorkspace ? new Thickness(2, 0, 2, 2) : new Thickness(4, 2, 4, 6);
        HeaderPrimaryRow.Height = HeaderSecondaryRow.Height = new GridLength(shortWorkspace ? 24 : 32);
        PlanContentGrid.Margin = shortWorkspace ? new Thickness(0, 2, 0, 2) : new Thickness(0, 6, 0, 4);
        BatchSelector.MinHeight = StatementSelector.MinHeight = 0;
        BatchSelector.Height = StatementSelector.Height = shortWorkspace ? 22 : double.NaN;
        HotspotsPaneButton.Visibility = shortWorkspace ? Visibility.Visible : Visibility.Collapsed;
        GraphPaneButton.Content = shortWorkspace ? "图" : "计划图";
        foreach (Button button in HeaderActions.Children.OfType<Button>())
        {
            button.MinHeight = 0;
            button.Height = shortWorkspace ? 22 : 28;
            button.Padding = shortWorkspace ? new Thickness(6, 0, 6, 0) : new Thickness(9, 2, 9, 2);
            button.FontWeight = FontWeights.Normal;
        }
        if (shortWorkspace) PlanGraphTabControl.SetResourceReference(Control.TemplateProperty, "GraphContentOnlyTemplate");
        else PlanGraphTabControl.ClearValue(Control.TemplateProperty);
    }

    private void ShowDetails()
    {
        _rightOpen = true;
        _activePane = PlanWorkspacePane.Details;
        StructuredDetailsTab.IsSelected = true;
        UpdateLayoutMode();
        QueueSavePreferences();
    }

    private void ShowGraph_Click(object sender, RoutedEventArgs e) => FocusGraph();
    private void ShowHotspots_Click(object sender, RoutedEventArgs e)
    {
        _activePane = PlanWorkspacePane.Graph;
        OperatorsTab.IsSelected = true;
        UpdateLayoutMode();
        Services.WorkspaceAccessibility.Focus(RecostGrid);
        QueueSavePreferences();
    }
    private void ToggleIssues_Click(object sender, RoutedEventArgs e)
    {
        bool visible = LeftPanel.Visibility == Visibility.Visible;
        _leftOpen = !visible;
        _activePane = visible ? PlanWorkspacePane.Graph : PlanWorkspacePane.Issues;
        UpdateLayoutMode();
        QueueSavePreferences();
    }
    private void ToggleDetails_Click(object sender, RoutedEventArgs e)
    {
        bool visible = RightPanel.Visibility == Visibility.Visible;
        _rightOpen = !visible;
        _activePane = visible ? PlanWorkspacePane.Graph : PlanWorkspacePane.Details;
        UpdateLayoutMode();
        QueueSavePreferences();
    }
    private void LayoutMenu_Click(object sender, RoutedEventArgs e)
    {
        var reset = new MenuItem { Header = "恢复默认布局与显示列" };
        reset.Click += (_, _) => { ApplyPreferences(new()); QueueSavePreferences(); };
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        menu.Items.Add(reset);
        menu.IsOpen = true;
    }
    private void OperatorColumns_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        foreach (var column in RecostGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            var item = new MenuItem { Header = column.Header, IsCheckable = true, IsChecked = column.Visibility == Visibility.Visible, StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                if (!item.IsChecked && RecostGrid.Columns.Count(candidate => candidate.Visibility == Visibility.Visible) == 1)
                {
                    item.IsChecked = true;
                    return;
                }
                column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                QueueSavePreferences();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private void PlanGraphTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PlanGraphTabControl)) QueueSavePreferences();
    }
    private void AuxiliaryPanel_Changed(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource)) return;
        UpdateLayoutMode();
        QueueSavePreferences();
    }
    private void AuxiliaryResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        _auxiliaryHeight = PlanWorkspaceLayoutService.AuxiliaryHeight(AuxiliaryContent.ActualHeight - e.VerticalChange, ActualHeight);
        UpdateLayoutMode();
        QueueSavePreferences();
    }
    private void PaneSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ReferenceEquals(sender, LeftSplitter)) _leftWidth = Math.Clamp(PlanContentGrid.ColumnDefinitions[0].ActualWidth, 220, 480);
        else _rightWidth = Math.Clamp(PlanContentGrid.ColumnDefinitions[4].ActualWidth, 260, 520);
        UpdateLayoutMode();
        QueueSavePreferences();
    }

    private void LeftPanel_Expanded(object sender, RoutedEventArgs e) => SidePanelChanged(sender, e, true, true);
    private void LeftPanel_Collapsed(object sender, RoutedEventArgs e) => SidePanelChanged(sender, e, true, false);
    private void RightPanel_Expanded(object sender, RoutedEventArgs e) => SidePanelChanged(sender, e, false, true);
    private void RightPanel_Collapsed(object sender, RoutedEventArgs e) => SidePanelChanged(sender, e, false, false);
    private void SidePanelChanged(object sender, RoutedEventArgs e, bool left, bool expanded)
    {
        if (!_preferencesLoaded || _applyingLayout || !ReferenceEquals(sender, e.OriginalSource) || PlanContentGrid == null) return;
        if (left)
        {
            _leftOpen = expanded;
            if (expanded) _activePane = PlanWorkspacePane.Issues;
            if (expanded) LeftPanelExpanded?.Invoke(sender, e); else LeftPanelCollapsed?.Invoke(sender, e);
        }
        else
        {
            _rightOpen = expanded;
            if (expanded) _activePane = PlanWorkspacePane.Details;
            if (expanded) RightPanelExpanded?.Invoke(sender, e); else RightPanelCollapsed?.Invoke(sender, e);
        }
        // Legacy subscribers may restore old column sizes. The responsive policy owns final widths.
        UpdateLayoutMode();
        QueueSavePreferences();
    }
}
