using Nodify;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer
{
    public enum DiagramViewMode
    {
        CostPercent,
        CpuIo,
        Rows
    }

    public enum PlanLayoutMode
    {
        Horizontal,
        Vertical
    }

    public enum PlanColorMode
    {
        TotalCost,
        CpuCost,
        IoCost
    }

    public enum LinkMetricMode
    {
        RowCount,
        DataSize
    }

    public partial class PlanGraphControl : UserControl, INotifyPropertyChanged
    {
        private static readonly PlanGraphCollapseUiActionService CollapseUiActionService = new();
        private static readonly PlanGraphConnectionUiActionService ConnectionUiActionService = new();
        private static readonly PlanGraphLayoutUiActionService LayoutUiActionService = new();
        private static readonly PlanGraphLoadUiActionService LoadUiActionService = new();
        private static readonly PlanGraphModeUiActionService ModeUiActionService = new();
        private static readonly PlanGraphNodeClipboardUiActionService NodeClipboardUiActionService = new();
        private static readonly PlanGraphPanUiActionService PanUiActionService = new();

        public ObservableCollection<PlanNodeViewModel> Nodes { get; } = new();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

        // Residual I/O 警告配置参数
        public static double ResidualIOThreshold { get; set; } = 10.0;
        public static int ResidualIOMinRowsRead { get; set; } = 1000;

        private XDocument? _currentDoc;
        private XNamespace? _currentNs;
        private List<PlanNodeViewModel> _masterNodes = new();
        private List<ConnectionViewModel> _masterConnections = new();

        private PlanLayoutMode _layoutMode = PlanLayoutMode.Horizontal;

        public double ArrowAngle
        {
            get
            {
                return LayoutMode == PlanLayoutMode.Horizontal ? 180 : -90;
            }
        }

        public PlanLayoutMode LayoutMode
        {
            get => _layoutMode;
            set
            {
                if (_layoutMode != value)
                {
                    _layoutMode = value;
                    OnPropertyChanged(nameof(LayoutMode));
                    ReapplyLayout();
                }
            }
        }

        private PlanColorMode _colorMode = PlanColorMode.TotalCost;
        public PlanColorMode ColorMode
        {
            get => _colorMode;
            set
            {
                if (_colorMode != value)
                {
                    _colorMode = value;
                    OnPropertyChanged(nameof(ColorMode));
                    ReapplyColorMode();
                }
            }
        }

        private LinkMetricMode _linkMetric = LinkMetricMode.RowCount;
        public LinkMetricMode LinkMetric
        {
            get => _linkMetric;
            set
            {
                if (_linkMetric != value)
                {
                    _linkMetric = value;
                    OnPropertyChanged(nameof(LinkMetric));
                    ReapplyLinkMetric();
                }
            }
        }

        private PlanNodeViewModel? _selectedNode;
        public PlanNodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (ReferenceEquals(_selectedNode, value)) return;
                _selectedNode = value;
                OnPropertyChanged(nameof(SelectedNode));
                // 选中时可通知宿主 (MainWindow) 刷新右侧属性面板
                NodeSelected?.Invoke(this, value);
                UpdateConnectionHighlights();
            }
        }

        public event EventHandler<PlanNodeViewModel?>? NodeSelected;
        public event EventHandler<PlanNodeViewModel?>? NodeDoubleClicked;
        public void OpenSelectedNode() { if (SelectedNode != null) NodeDoubleClicked?.Invoke(this, SelectedNode); }
        public void FocusFirstNode()
        {
            if (Nodes.Count == 0) { Focus(); return; }
            FocusNode(SelectedNode != null && Nodes.Contains(SelectedNode) ? SelectedNode : Nodes[0]);
        }
        private async void FocusNode(PlanNodeViewModel node)
        {
            try
            {
                SelectKeyboardNode(node);
                long intentRevision = _viewportIntentRevision;
                await PendingPage;
                // Focus itself triggers AccessiblePlanNode's selection hook,
                // which centers the node. Do not let an old cross-page focus
                // overwrite a newer fit/zoom or steal focus from its button.
                if (intentRevision != _viewportIntentRevision || !ReferenceEquals(SelectedNode, node) || !Nodes.Contains(node)) return;
                UpdateLayout();
                var visual = Services.WorkspaceAccessibility.Descendants(Editor).OfType<AccessiblePlanNode>().FirstOrDefault(item => ReferenceEquals(item.DataContext, node));
                if (visual != null) Services.WorkspaceAccessibility.Focus(visual);
                else Editor.Focus();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { WorkspaceAccessibility.Report(exception, "GraphFocus", this); }
        }
        private void Graph_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is not AccessiblePlanNode && !ReferenceEquals(e.OriginalSource, Editor)) return;
            if (Keyboard.Modifiers != ModifierKeys.None || Nodes.Count == 0) return;
            try
            {
                if (e.Key == Key.Enter) OpenSelectedNode();
                else if (e.Key is Key.Left or Key.Up or Key.Right or Key.Down or Key.Home or Key.End)
                {
                    var available = _masterNodes.Where(node => node.IsVisible).ToList();
                    if (available.Count == 0) return;
                    int index = e.Key == Key.Home ? 0 : e.Key == Key.End ? available.Count - 1 :
                        Core.Services.WorkspaceInteractionService.MoveSelection(available.Count, SelectedNode == null ? -1 : available.IndexOf(SelectedNode), e.Key is Key.Left or Key.Up ? -1 : 1);
                    FocusNode(available[index]);
                }
                else return;
                e.Handled = true;
            }
            catch (Exception exception) { Services.WorkspaceAccessibility.Report(exception, "GraphKeyboard", this); e.Handled = true; }
        }
        public void SelectKeyboardNode(PlanNodeViewModel node)
        {
            if (!_masterNodes.Contains(node)) return;
            if (node.Identity != null) SelectOperator(node.Identity);
            else SelectedNode = node;
        }

        private Core.Services.PlanGraphPanState _panState = new(false, new Point());

        private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _panState = PanUiActionService.BeginPan(
                e.OriginalSource,
                e.GetPosition(this),
                () => Editor.CaptureMouse(),
                _panState);
        }

        private void Editor_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _panState = PanUiActionService.Pan(
                _panState,
                e.GetPosition(this),
                Editor.ViewportLocation,
                Editor.ViewportZoom,
                Editor.PanViewport);
        }

        private void Editor_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _panState = PanUiActionService.EndPan(
                _panState,
                Editor.ReleaseMouseCapture);
        }

        private void Node_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Nodify.ItemContainer container && container.DataContext is PlanNodeViewModel node)
            {
                NodeDoubleClicked?.Invoke(this, node);
            }
        }

        public PlanGraphControl()
        {
            InitializeComponent();
            DataContext = this;
            InitializePresentation();

            Editor.DisablePanning = false;
            Editor.DisableZooming = false;
            // Node dragging is enabled by default via ItemContainer in v6; optimizations are static/class level
            NodifyEditor.EnableDraggingContainersOptimizations = true;

            // 默认显示提示，加载真实数据后隐藏
            ShowEmptyHint(true);
            // 保留少量示例（仅设计时参考，运行时由宿主调用 Load 覆盖）
        }

        private void ShowEmptyHint(bool show)
        {
            var hint = FindName("EmptyHint") as TextBlock;
            if (hint != null) hint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 核心：从真实执行计划 XDocument 加载可拖拽节点图 (Plan Explorer 风格)
        /// </summary>
        public void LoadFromExecutionPlan(XDocument doc, XNamespace ns, IReadOnlyList<XElement>? operators = null,
            Core.Rules.PlanDiagnosticReport? diagnostics = null)
        {
            InvalidateGraph();
            ShowEmptyHint(false);

            PlanLayoutMode initialLayout = CmbLayoutMode != null && CmbLayoutMode.SelectedIndex >= 0 ? (PlanLayoutMode)CmbLayoutMode.SelectedIndex : PlanLayoutMode.Horizontal;
            PlanColorMode initialColor = CmbColorMode != null && CmbColorMode.SelectedIndex >= 0 ? (PlanColorMode)CmbColorMode.SelectedIndex : PlanColorMode.TotalCost;
            DiagramViewMode initialView = CmbViewMode != null && CmbViewMode.SelectedIndex >= 0 ? (DiagramViewMode)CmbViewMode.SelectedIndex : DiagramViewMode.CostPercent;
            LinkMetricMode initialLinkMetric = CmbLinkMetric != null && CmbLinkMetric.SelectedIndex >= 0 ? (LinkMetricMode)CmbLinkMetric.SelectedIndex : LinkMetricMode.RowCount;

            _layoutMode = initialLayout;
            _colorMode = initialColor;
            _linkMetric = initialLinkMetric;

            PlanGraphLoadUiActionResult result =
                LoadUiActionService.Load(
                    doc,
                    ns,
                    Nodes,
                    Connections,
                    new PlanGraphLoadUiActionOptions
                    {
                        Operators = operators,
                        Diagnostics = diagnostics,
                        InitialLayout = initialLayout,
                        InitialColor = initialColor,
                        InitialView = initialView,
                        InitialLinkMetric = initialLinkMetric,
                        ResidualIoThreshold = ResidualIOThreshold,
                        ResidualIoMinRowsRead = ResidualIOMinRowsRead
                    });

            _currentDoc = doc;
            _currentNs = ns;
            _masterNodes = result.MasterNodes.ToList();
            _masterConnections = result.MasterConnections.ToList();
            ApplyAutomaticViewMode();
            OnPropertyChanged(nameof(AllNodes));
            _pageStart = 0;
            PageDescription = Core.Services.PlanGraphPageService.ForIndex(_masterNodes.Count, 0).Description;
            OnPropertyChanged(nameof(PageDescription)); OnPropertyChanged(nameof(CanPreviousPage)); OnPropertyChanged(nameof(CanNextPage));
            SelectedNode = result.SelectedNode;
            RefreshPageContext(Nodes.ToArray());
            if (_masterNodes.Count > Core.Services.PlanGraphPageService.MaximumVisibleNodes)
                _ = NavigatePageAsync(0);
            EmptyHint.Text = doc.Root == null
                ? "尚未加载执行计划，请打开 .sqlplan 或 .xml 文件。"
                : "当前选择未采集可显示的算子。\n请切换语句/计划并核对诊断运行状态。\n空图不构成健康结论。";
            ShowEmptyHint(!result.HasGraph);
            if (_masterNodes.Count <= Core.Services.PlanGraphPageService.MaximumVisibleNodes)
                ApplyPendingViewport();
        }

        public void SelectOperator(Core.Models.PlanOperatorKey key)
        {
            var node = _masterNodes.SingleOrDefault(n => n.Identity == key);
            if (node == null) throw new System.IO.InvalidDataException("证据节点不在当前图范围内。");
            CollapseUiActionService.RevealNode(node, _masterNodes, ReapplyLayout, UpdateGraphVisibility);
            RevealPage(node);
            SelectedNode = node;
            CenterNode(node);
        }

        public void ResetView() => ResetZoom();


        private void CopyNodeInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is PlanNodeViewModel node)
            {
                string text = NodeClipboardUiActionService.BuildNodeInfo(node);
                System.Windows.Clipboard.SetText(text);

                ToastPopup.IsOpen = true;
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => ToastPopup.IsOpen = false));

            }
        }

        private void ResetView_Click(object sender, RoutedEventArgs e) => ResetView();

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            CollapseUiActionService.ApplyCollapseStates(
                _masterNodes,
                CollapseUiActionService.CalculateExpandAll(_masterNodes));
            UpdateGraphVisibility();
            ReapplyLayout();
        }

        private void SmartCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            CollapseUiActionService.ApplyCollapseStates(
                _masterNodes,
                CollapseUiActionService.CalculateSmartCollapse(_masterNodes));

            UpdateGraphVisibility();
            ReapplyLayout();
        }

        private void ToggleCollapse_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button btn && btn.DataContext is PlanNodeViewModel node)
            {
                CollapseUiActionService.ToggleNode(
                    node,
                    _masterNodes,
                    _masterConnections,
                    ReapplyLayout,
                    UpdateGraphVisibility);
            }
        }

        private void UpdateGraphVisibility()
        {
            if (_masterNodes.Count > Core.Services.PlanGraphPageService.MaximumVisibleNodes)
            {
                CollapseUiActionService.UpdateVisibility(_currentDoc, _currentNs, _masterNodes, _masterConnections,
                    new HashSet<PlanNodeViewModel>(), new HashSet<ConnectionViewModel>());
                _ = NavigatePageAsync(_pageStart);
                return;
            }
            CollapseUiActionService.UpdateVisibility(
                _currentDoc,
                _currentNs,
                _masterNodes,
                _masterConnections,
                Nodes,
                Connections);
            RefreshPageContext(Nodes.ToArray());
        }

        private void CmbViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbViewMode == null || Nodes == null) return;
            if (_presentationReady && !_applyingPreferences) _hasExplicitViewMode = true;
            ModeUiActionService.ApplyViewMode(CmbViewMode.SelectedIndex, _masterNodes);
            OnPropertyChanged(nameof(GraphLegend));
            NotifyPreferencesChanged();
        }

        private void CmbLayoutMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLayoutMode == null || Nodes == null) return;
            ModeUiActionService.ApplyLayoutMode(
                CmbLayoutMode.SelectedIndex,
                mode => LayoutMode = mode);
            NotifyPreferencesChanged();
        }

        private void CmbColorMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbColorMode == null || Nodes == null) return;
            ModeUiActionService.ApplyColorMode(
                CmbColorMode.SelectedIndex,
                mode => ColorMode = mode);
            NotifyPreferencesChanged();
        }

        private void CmbLinkMetric_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLinkMetric == null || Nodes == null) return;
            ModeUiActionService.ApplyLinkMetric(
                CmbLinkMetric.SelectedIndex,
                metric => LinkMetric = metric);
            NotifyPreferencesChanged();
        }

        private void ReapplyLayout()
        {
            if (_masterNodes.Count > Core.Services.PlanGraphPageService.MaximumVisibleNodes)
            {
                foreach (var connection in _masterConnections) connection.LayoutMode = LayoutMode;
                _ = NavigatePageAsync(_pageStart);
                return;
            }
            LayoutUiActionService.ReapplyLayout(
                _currentDoc,
                _currentNs,
                _masterNodes,
                _masterConnections,
                LayoutMode);
        }

        private void ReapplyColorMode()
        {
            ModeUiActionService.ApplyColorMode(ColorMode, _masterNodes);
        }

        private void ReapplyLinkMetric()
        {
            ModeUiActionService.ApplyLinkMetric(LinkMetric, _masterConnections);
        }

        private void UpdateConnectionHighlights()
        {
            ConnectionUiActionService.UpdateHighlights(_selectedNode?.SelectionKey, Connections);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}
