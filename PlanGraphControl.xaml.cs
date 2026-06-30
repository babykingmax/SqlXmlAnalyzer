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
        private static readonly Core.Services.PlanGraphPanInteractionService PanInteractionService = new();
        private static readonly PlanGraphViewportUiActionService ViewportUiActionService = new();

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
                _selectedNode = value;
                OnPropertyChanged(nameof(SelectedNode));
                // 选中时可通知宿主 (MainWindow) 刷新右侧属性面板
                NodeSelected?.Invoke(this, value);
                UpdateConnectionHighlights();
            }
        }

        public event EventHandler<PlanNodeViewModel?>? NodeSelected;
        public event EventHandler<PlanNodeViewModel?>? NodeDoubleClicked;

        private Core.Services.PlanGraphPanState _panState = new(false, new Point());

        private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is PlanNodeViewModel) return;
            if (e.OriginalSource is FrameworkElement fe2 && fe2.DataContext is ConnectionViewModel) return;

            _panState = PanInteractionService.Begin(e.GetPosition(this));
            Editor.CaptureMouse();
        }

        private void Editor_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            Core.Services.PlanGraphPanUpdate? update =
                PanInteractionService.Pan(
                    _panState,
                    e.GetPosition(this),
                    Editor.ViewportLocation,
                    Editor.ViewportZoom);

            if (update != null)
            {
                _panState = update.State;
                Editor.ViewportLocation = update.ViewportLocation;
            }
        }

        private void Editor_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_panState.IsPanning)
            {
                _panState = PanInteractionService.End(_panState);
                Editor.ReleaseMouseCapture();
            }
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
        public void LoadFromExecutionPlan(XDocument doc, XNamespace ns)
        {
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
                        InitialLayout = initialLayout,
                        InitialColor = initialColor,
                        InitialView = initialView,
                        InitialLinkMetric = initialLinkMetric,
                        ResidualIoThreshold = ResidualIOThreshold,
                        ResidualIoMinRowsRead = ResidualIOMinRowsRead
                    });

            if (!result.HasGraph)
            {
                ShowEmptyHint(true);
                return;
            }

            _currentDoc = doc;
            _currentNs = ns;
            _masterNodes = result.MasterNodes.ToList();
            _masterConnections = result.MasterConnections.ToList();
            SelectedNode = result.SelectedNode;
        }

        public void ResetView()
        {
            ViewportUiActionService.ResetView(
                zoom => Editor.ViewportZoom = zoom,
                Nodes);
        }


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

        private void ToggleCollapse_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true; // MUST PREVENT NodifyEditor from capturing this click!
                if (sender is Button btn && btn.DataContext is PlanNodeViewModel node)
                {
                    var logService = new Core.Services.PlanGraphCollapseLogService();
                    DateTime timestamp = DateTime.Now;
                    Core.Services.PlanGraphCollapseLogNode nodeBeforeToggle =
                        CollapseUiActionService.ToCollapseLogNode(node);
                    CollapseUiActionService.AppendCollapseLog(logService.BuildStartLine(nodeBeforeToggle, timestamp));
                    Core.Services.PlanGraphCollapseLogSnapshot oldSnapshot =
                        CollapseUiActionService.CaptureLogSnapshot(
                            _masterNodes,
                            _masterConnections);

                    // 仅切换当前节点的折叠状态，保留其子孙节点原有的折叠状态（状态记忆）
                    if (node.RawElement != null)
                    {
                        CollapseUiActionService.ApplyCollapseStates(
                            _masterNodes,
                            CollapseUiActionService.CalculateToggle(
                                _masterNodes,
                                node.RawElement));
                    }
                    else
                    {
                        node.IsCollapsed = !node.IsCollapsed;
                    }

                    // 1. 先在完整树上计算所有节点的新绝对坐标
                    ReapplyLayout();

                    // 2. 根据最新的折叠状态更新 IsVisible，触发 Nodify 容器隐藏/显示
                    UpdateGraphVisibility();
                    Core.Services.PlanGraphCollapseLogSnapshot newSnapshot =
                        CollapseUiActionService.CaptureLogSnapshot(
                            _masterNodes,
                            _masterConnections);
                    CollapseUiActionService.AppendCollapseLog(
                        logService.BuildToggleLog(
                            nodeBeforeToggle,
                            node.IsCollapsed,
                            oldSnapshot,
                            newSnapshot,
                            timestamp));
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var logService = new Core.Services.PlanGraphCollapseLogService();
                    CollapseUiActionService.AppendCollapseLog(logService.BuildExceptionLog(ex, DateTime.Now));
                }
                catch { }
            }
        }

        private void UpdateGraphVisibility()
        {
            CollapseUiActionService.UpdateVisibility(
                _currentDoc,
                _currentNs,
                _masterNodes,
                _masterConnections,
                Nodes,
                Connections);
        }

        private void CmbViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbViewMode == null || Nodes == null) return;
            ModeUiActionService.ApplyViewMode(CmbViewMode.SelectedIndex, Nodes);
        }

        private void CmbLayoutMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLayoutMode == null || Nodes == null) return;
            ModeUiActionService.ApplyLayoutMode(
                CmbLayoutMode.SelectedIndex,
                mode => LayoutMode = mode);
        }

        private void CmbColorMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbColorMode == null || Nodes == null) return;
            ModeUiActionService.ApplyColorMode(
                CmbColorMode.SelectedIndex,
                mode => ColorMode = mode);
        }

        private void CmbLinkMetric_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLinkMetric == null || Nodes == null) return;
            ModeUiActionService.ApplyLinkMetric(
                CmbLinkMetric.SelectedIndex,
                metric => LinkMetric = metric);
        }

        private void ReapplyLayout()
        {
            LayoutUiActionService.ReapplyLayout(
                _currentDoc,
                _currentNs,
                _masterNodes,
                _masterConnections,
                LayoutMode);
        }

        private void ReapplyColorMode()
        {
            ModeUiActionService.ApplyColorMode(ColorMode, Nodes);
        }

        private void ReapplyLinkMetric()
        {
            ModeUiActionService.ApplyLinkMetric(LinkMetric, Connections);
        }

        private void UpdateConnectionHighlights()
        {
            ConnectionUiActionService.UpdateHighlights(_selectedNode?.NodeId, Connections);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}
