using Nodify;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;

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
        private static readonly Core.Services.PlanGraphCollapseStateService CollapseStateService = new();
        private static readonly Core.Services.PlanGraphConnectionBuilderService ConnectionBuilderService = new();
        private static readonly Core.Services.PlanGraphCostCalculationService CostCalculationService = new();
        private static readonly Core.Services.PlanGraphLayoutRefreshService LayoutRefreshService = new();
        private static readonly Core.Services.PlanGraphMissingIndexAssociationService MissingIndexAssociationService = new();
        private static readonly Core.Services.PlanGraphNodeBuilderService NodeBuilderService = new();
        private static readonly Core.Services.PlanGraphVisibilityStateService VisibilityStateService = new();

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

        private Point _lastMousePosition;
        private bool _isPanning;

        private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is PlanNodeViewModel) return;
            if (e.OriginalSource is FrameworkElement fe2 && fe2.DataContext is ConnectionViewModel) return;

            _isPanning = true;
            _lastMousePosition = e.GetPosition(this);
            Editor.CaptureMouse();
        }

        private void Editor_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var currentPosition = e.GetPosition(this);
                var delta = currentPosition - _lastMousePosition;
                Editor.ViewportLocation = new Point(Editor.ViewportLocation.X - delta.X / Editor.ViewportZoom, Editor.ViewportLocation.Y - delta.Y / Editor.ViewportZoom);
                _lastMousePosition = currentPosition;
            }
        }

        private void Editor_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
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
            Nodes.Clear();
            Connections.Clear();
            ShowEmptyHint(false);

            if (doc?.Root == null)
            {
                ShowEmptyHint(true);
                return;
            }

            var relOps = doc.Descendants(ns + "RelOp").ToList();
            if (relOps.Count == 0)
            {
                ShowEmptyHint(true);
                return;
            }

            _currentDoc = doc;
            _currentNs = ns;

            PlanLayoutMode initialLayout = CmbLayoutMode != null && CmbLayoutMode.SelectedIndex >= 0 ? (PlanLayoutMode)CmbLayoutMode.SelectedIndex : PlanLayoutMode.Horizontal;
            PlanColorMode initialColor = CmbColorMode != null && CmbColorMode.SelectedIndex >= 0 ? (PlanColorMode)CmbColorMode.SelectedIndex : PlanColorMode.TotalCost;
            DiagramViewMode initialView = CmbViewMode != null && CmbViewMode.SelectedIndex >= 0 ? (DiagramViewMode)CmbViewMode.SelectedIndex : DiagramViewMode.CostPercent;
            LinkMetricMode initialLinkMetric = CmbLinkMetric != null && CmbLinkMetric.SelectedIndex >= 0 ? (LinkMetricMode)CmbLinkMetric.SelectedIndex : LinkMetricMode.RowCount;

            _layoutMode = initialLayout;
            _colorMode = initialColor;
            _linkMetric = initialLinkMetric;

            // 1. 解析所有 RelOp 到 ViewModel (带 Object / 警告 / 并行信息)
            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            var allNodes = new List<PlanNodeViewModel>();

            foreach (var relOp in relOps)
            {
                var vm = CreateNodeFromRelOp(relOp, ns);
                nodeMap[relOp] = vm;
                allNodes.Add(vm);
            }

            var missingIndexes = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, ns);
            IReadOnlyList<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion?> matchedSuggestions =
                MissingIndexAssociationService.MatchSuggestions(
                    allNodes
                        .Select(node => new Core.Services.PlanGraphMissingIndexNodeInfo(
                            node.TableName))
                        .ToList(),
                    missingIndexes);
            for (int i = 0; i < allNodes.Count; i++)
            {
                allNodes[i].AssociatedSuggestion = matchedSuggestions[i];
            }

            ApplyCostCalculations(
                relOps,
                nodeMap,
                ns,
                initialView,
                initialColor);

            // 2. 简单分层初始布局 (类似 Plan Explorer 水平/垂直流)
            ApplyLayeredLayout(nodeMap, relOps, ns);

            foreach (Core.Services.PlanGraphConnectionPair connection in
                ConnectionBuilderService.BuildConnections(relOps, ns))
            {
                if (nodeMap.TryGetValue(connection.SourceRelOp, out PlanNodeViewModel? sourceVm)
                    && nodeMap.TryGetValue(connection.TargetRelOp, out PlanNodeViewModel? targetVm))
                {
                    Connections.Add(new ConnectionViewModel
                    {
                        Source = sourceVm,
                        Target = targetVm,
                        LayoutMode = initialLayout,
                        CurrentLinkMetric = initialLinkMetric
                    });
                }
            }

            // 添加到集合 (Nodify 会自动响应)
            _masterNodes = allNodes;
            _masterConnections = Connections.ToList();
            foreach (var n in allNodes) Nodes.Add(n);

            // 默认选中根节点 (最高成本或第一个)
            SelectedNode = allNodes.OrderByDescending(n => n.CostPercent).FirstOrDefault() ?? allNodes.FirstOrDefault();
        }

        private static void ApplyCostCalculations(
            List<XElement> relOps,
            Dictionary<XElement, PlanNodeViewModel> nodeMap,
            XNamespace ns,
            DiagramViewMode initialView,
            PlanColorMode initialColor)
        {
            var inputs = new List<Core.Services.PlanGraphNodeCostInput>();

            foreach (XElement relOp in relOps)
            {
                PlanNodeViewModel vm = nodeMap[relOp];
                List<XElement> childRelOps =
                    PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns).ToList();
                vm.HasChildren = childRelOps.Count > 0;
                List<double> childSubtreeCosts = childRelOps
                    .Select(child =>
                    {
                        if (nodeMap.TryGetValue(child, out PlanNodeViewModel? childVm))
                        {
                            return childVm.SubtreeCost;
                        }

                        return safeFloat(
                            child.Attribute("EstimatedTotalSubtreeCost")?.Value);
                    })
                    .ToList();

                inputs.Add(new Core.Services.PlanGraphNodeCostInput(
                    vm.SubtreeCost,
                    childSubtreeCosts,
                    vm.EstimatedCPUCostNum,
                    vm.EstimatedIOCostNum,
                    vm.EstRowsNum,
                    vm.ActualRowsNum,
                    !string.IsNullOrEmpty(vm.ActualRows)));
            }

            IReadOnlyList<Core.Services.PlanGraphNodeCostResult> results =
                CostCalculationService.Calculate(inputs);

            for (int i = 0; i < relOps.Count; i++)
            {
                PlanNodeViewModel vm = nodeMap[relOps[i]];
                Core.Services.PlanGraphNodeCostResult result = results[i];
                vm.OwnCost = result.OwnCost;
                vm.Cost = result.DisplayCost;
                vm.ActualRecost = result.ActualRecost;
                vm.CostPercent = result.CostPercent;
                vm.CpuPercent = result.CpuPercent;
                vm.IoPercent = result.IoPercent;
                vm.ViewMode = initialView;
                vm.ColorMode = initialColor;
            }
        }

        private static double safeFloat(string? val, double defaultValue = 0.0)
        {
            if (string.IsNullOrEmpty(val)) return defaultValue;
            if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                return parsed;
            return defaultValue;
        }

        private PlanNodeViewModel CreateNodeFromRelOp(XElement relOp, XNamespace ns)
        {
            Core.Services.PlanGraphNodeBuildResult node =
                NodeBuilderService.Build(
                    relOp,
                    ns,
                    new Core.Services.PlanGraphNodeWarningSettings(
                        ResidualIOThreshold,
                        ResidualIOMinRowsRead));

            var vm = new PlanNodeViewModel
            {
                RawElement = node.RawElement,
                NodeId = node.NodeId,
                PhysicalOp = node.PhysicalOp,
                LogicalOp = node.LogicalOp,
                ExecutionMode = node.ExecutionMode,
                Cost = node.Cost,
                OwnCost = node.OwnCost,
                ActualRecost = node.ActualRecost,
                SubtreeCost = node.SubtreeCost,
                CostPercent = node.CostPercent,
                EstRows = node.EstRows,
                EstRowsNum = node.EstRowsNum,
                EstimatedRowsToBeRead = node.EstimatedRowsToBeRead,
                EstimatedCPUCostNum = node.EstimatedCPUCostNum,
                EstimatedIOCostNum = node.EstimatedIOCostNum,
                AvgRowSizeNum = node.AvgRowSizeNum,
                EstimatedIOCost = node.EstimatedIOCost,
                EstimatedCPUCost = node.EstimatedCPUCost,
                EstimatedExecutions = node.EstimatedExecutions,
                ActualExecutions = node.ActualExecutions,
                ActualRows = node.ActualRows,
                ActualRowsRead = node.ActualRowsRead,
                ActualRowsNum = node.ActualRowsNum,
                EstimatedOperatorCost = node.EstimatedOperatorCost,
                EstimatedSubtreeCostStr = node.EstimatedSubtreeCostStr,
                EstimatedRowSize = node.EstimatedRowSize,
                EstimatedDataSize = node.EstimatedDataSize,
                ActualDataSize = node.ActualDataSize,
                ActualRebinds = node.ActualRebinds,
                ActualRewinds = node.ActualRewinds,
                Ordered = node.Ordered,
                DatabaseName = node.DatabaseName,
                TableName = node.TableName,
                IndexName = node.IndexName,
                SeekPredicates = node.SeekPredicates,
                Predicate = node.Predicate,
                OutputList = node.OutputList,
                ObjectDetails = node.ObjectDetails,
                Partitioned = node.Partitioned,
                PartitionCount = node.PartitionCount,
                PartitionRange = node.PartitionRange,
                IsParallel = node.IsParallel,
                Warnings = node.Warnings,
                NodeSeverity = node.NodeSeverity,
                OperatorType = node.OperatorType,
                Location = new Point(50, 50)
            };

            var iconInfo = PhysicalOpToIconMapper.Map(node.PhysicalOp);
            vm.IconGeometry = iconInfo.Geometry;
            vm.IconBrush = iconInfo.Brush;

            return vm;
        }

        private Core.Services.PlanGraphLayoutRefreshResult ApplyLayeredLayout(
            Dictionary<XElement, PlanNodeViewModel> nodeMap,
            List<XElement> allRelOps,
            XNamespace ns)
        {
            Core.Services.PlanGraphLayoutRefreshResult result =
                LayoutRefreshService.Calculate(
                    allRelOps,
                    ns,
                    nodeMap
                        .Select(pair => new Core.Services.PlanGraphLayoutRefreshNode(
                            pair.Key,
                            pair.Value.IsCollapsed))
                        .ToList(),
                    ToGraphLayoutDirection(LayoutMode));

            foreach (Core.Services.PlanGraphLayoutRefreshPosition position in result.NodePositions)
            {
                if (nodeMap.TryGetValue(position.RelOp, out PlanNodeViewModel? vm))
                {
                    vm.SubtreeWidth = position.SubtreeWidth;
                    vm.Location = new Point(position.X, position.Y);
                }
            }

            return result;
        }

        private static Core.Services.PlanGraphLayoutDirection ToGraphLayoutDirection(
            PlanLayoutMode layoutMode)
        {
            return layoutMode == PlanLayoutMode.Horizontal
                ? Core.Services.PlanGraphLayoutDirection.Horizontal
                : Core.Services.PlanGraphLayoutDirection.Vertical;
        }

        private static PlanLayoutMode ToPlanLayoutMode(
            Core.Services.PlanGraphLayoutDirection layoutDirection)
        {
            return layoutDirection == Core.Services.PlanGraphLayoutDirection.Horizontal
                ? PlanLayoutMode.Horizontal
                : PlanLayoutMode.Vertical;
        }

        public void ResetView()
        {
            Editor.ViewportZoom = 1.0;
            if (Nodes.Count > 0)
            {
                var first = Nodes[0].Location;
                Nodes[0].Location = new Point(first.X + 1, first.Y);
                Nodes[0].Location = first;
            }
        }


        private void CopyNodeInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is PlanNodeViewModel node)
            {
                var clipboardService = new Core.Services.PlanGraphNodeClipboardService();
                string text = clipboardService.BuildNodeInfo(ToClipboardInfo(node));
                System.Windows.Clipboard.SetText(text);

                ToastPopup.IsOpen = true;
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => ToastPopup.IsOpen = false));

            }
        }

        private static Core.Services.PlanGraphNodeClipboardInfo ToClipboardInfo(
            PlanNodeViewModel node)
        {
            return new Core.Services.PlanGraphNodeClipboardInfo(
                node.NodeId,
                node.PhysicalOp,
                node.LogicalOp,
                node.SubtreeCost,
                node.CostPercent,
                node.EstRows,
                node.ActualRows,
                node.EstimatedDataSize,
                node.ObjectDetails,
                node.OutputList,
                node.SeekPredicates,
                node.Predicate,
                node.Warnings);
        }

        private void ResetView_Click(object sender, RoutedEventArgs e) => ResetView();

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            ApplyCollapseStates(
                CollapseStateService.CalculateExpandAll(
                    BuildCollapseStateNodes()).CollapsedStates);
            UpdateGraphVisibility();
            ReapplyLayout();
        }

        private void SmartCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            ApplyCollapseStates(
                CollapseStateService.CalculateSmartCollapse(
                    BuildCollapseStateNodes()).CollapsedStates);

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
                    Core.Services.PlanGraphCollapseLogNode nodeBeforeToggle = ToCollapseLogNode(node);
                    AppendCollapseLog(logService.BuildStartLine(nodeBeforeToggle, timestamp));
                    Core.Services.PlanGraphCollapseLogSnapshot oldSnapshot =
                        CaptureCollapseLogSnapshot();

                    // 仅切换当前节点的折叠状态，保留其子孙节点原有的折叠状态（状态记忆）
                    if (node.RawElement != null)
                    {
                        ApplyCollapseStates(
                            CollapseStateService.CalculateToggle(
                                BuildCollapseStateNodes(),
                                node.RawElement).CollapsedStates);
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
                        CaptureCollapseLogSnapshot();
                    AppendCollapseLog(
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
                    AppendCollapseLog(logService.BuildExceptionLog(ex, DateTime.Now));
                }
                catch { }
            }
        }

        private IReadOnlyList<Core.Services.PlanGraphCollapseStateNode> BuildCollapseStateNodes()
        {
            return _masterNodes
                .Where(node => node.RawElement != null)
                .Select(node => new Core.Services.PlanGraphCollapseStateNode(
                    node.RawElement!,
                    node.HasChildren,
                    node.SubtreeCost,
                    node.NodeSeverity,
                    node.IsCollapsed))
                .ToList();
        }

        private void ApplyCollapseStates(
            IReadOnlyDictionary<XElement, bool> collapsedStates)
        {
            foreach (var node in _masterNodes)
            {
                node.IsCollapsed =
                    node.RawElement != null
                    && collapsedStates.TryGetValue(node.RawElement, out bool isCollapsed)
                    && isCollapsed;
            }
        }

        private Core.Services.PlanGraphCollapseLogSnapshot CaptureCollapseLogSnapshot()
        {
            return new Core.Services.PlanGraphCollapseLogSnapshot(
                _masterNodes
                    .Where(n => n.IsVisible)
                    .Select(ToCollapseLogNode)
                    .ToList(),
                _masterConnections
                    .Where(c => c.IsVisible)
                    .Select(ToCollapseLogConnection)
                    .ToList());
        }

        private static Core.Services.PlanGraphCollapseLogNode ToCollapseLogNode(
            PlanNodeViewModel node)
        {
            return new Core.Services.PlanGraphCollapseLogNode(
                node.NodeId,
                node.PhysicalOp,
                node.IsCollapsed);
        }

        private static Core.Services.PlanGraphCollapseLogConnection ToCollapseLogConnection(
            ConnectionViewModel connection)
        {
            return new Core.Services.PlanGraphCollapseLogConnection(
                connection.Source?.NodeId,
                connection.Source?.PhysicalOp,
                connection.Target?.NodeId,
                connection.Target?.PhysicalOp);
        }

        private static void AppendCollapseLog(string text)
        {
            string logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
            string logFile = System.IO.Path.Combine(logDir, "CollapseLog.txt");
            System.IO.File.AppendAllText(logFile, text);
        }

        private void UpdateGraphVisibility()
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();

            IReadOnlyList<Core.Services.PlanGraphVisibilityStateNode> visibilityNodes =
                _masterNodes
                    .Where(node => node.RawElement != null)
                    .Select(node => new Core.Services.PlanGraphVisibilityStateNode(
                        node.RawElement!,
                        node.IsCollapsed))
                    .ToList();
            IReadOnlyList<Core.Services.PlanGraphVisibilityStateConnection> visibilityConnections =
                _masterConnections
                    .Where(connection =>
                        connection.Source?.RawElement != null
                        && connection.Target?.RawElement != null)
                    .Select(connection => new Core.Services.PlanGraphVisibilityStateConnection(
                        connection.Source!.RawElement!,
                        connection.Target!.RawElement!))
                    .ToList();

            Core.Services.PlanGraphVisibilityStateResult visibility =
                VisibilityStateService.Calculate(
                    relOps,
                    _currentNs,
                    visibilityNodes,
                    visibilityConnections);

            // ==================================================
            // 完全基于 Nodify 推荐的设计模式：纯数据绑定
            // 不再动态向 ObservableCollection 中 Add/Remove 节点，
            // 而是所有节点都在集合中，仅通过更新 IsVisible 属性，
            // 让 ItemContainerStyle 中的 Visibility 绑定自动接管显示隐藏。
            // 这样彻底避免了虚拟化面板的集合变更 Bug 和动画丢失问题。
            // ==================================================

            foreach (var n in _masterNodes)
            {
                n.IsVisible = n.RawElement != null
                    && visibility.VisibleRelOps.Contains(n.RawElement);
                if (!Nodes.Contains(n)) Nodes.Add(n); // 确保集合包含全部节点（通常初始化时已包含）
            }

            foreach (var c in _masterConnections)
            {
                c.IsVisible =
                    c.Source?.RawElement != null
                    && c.Target?.RawElement != null
                    && visibility.VisibleConnections.Contains(
                        new Core.Services.PlanGraphVisibilityStateConnection(
                            c.Source.RawElement,
                            c.Target.RawElement));
                if (!Connections.Contains(c)) Connections.Add(c); // 确保集合包含全部连接线
            }
        }

        private void CmbViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbViewMode == null || Nodes == null) return;
            var mode = (DiagramViewMode)CmbViewMode.SelectedIndex;
            foreach (var node in Nodes)
            {
                node.ViewMode = mode;
            }
        }

        private void CmbLayoutMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLayoutMode == null || Nodes == null) return;
            LayoutMode = (PlanLayoutMode)CmbLayoutMode.SelectedIndex;
        }

        private void CmbColorMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbColorMode == null || Nodes == null) return;
            ColorMode = (PlanColorMode)CmbColorMode.SelectedIndex;
        }

        private void CmbLinkMetric_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLinkMetric == null || Nodes == null) return;
            LinkMetric = (LinkMetricMode)CmbLinkMetric.SelectedIndex;
        }

        private void ReapplyLayout()
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();
            if (relOps.Count == 0) return;

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in _masterNodes)
            {
                if (node.RawElement != null)
                {
                    nodeMap[node.RawElement] = node;
                }
            }

            Core.Services.PlanGraphLayoutRefreshResult layout =
                ApplyLayeredLayout(nodeMap, relOps, _currentNs);

            foreach (var conn in _masterConnections)
            {
                conn.LayoutMode = ToPlanLayoutMode(layout.ConnectionLayout);
            }
        }

        private void ReapplyColorMode()
        {
            foreach (var node in Nodes)
            {
                node.ColorMode = ColorMode;
            }
        }

        private void ReapplyLinkMetric()
        {
            foreach (var conn in Connections)
            {
                conn.CurrentLinkMetric = LinkMetric;
            }
        }

        private void UpdateConnectionHighlights()
        {
            var highlightService = new Core.Services.PlanGraphConnectionHighlightService();
            string? selectedNodeId = _selectedNode?.NodeId;

            foreach (var conn in Connections)
            {
                conn.IsHighlighted = highlightService.ShouldHighlight(
                    selectedNodeId,
                    conn.Source?.NodeId,
                    conn.Target?.NodeId);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PlanNodeViewModel : INotifyPropertyChanged
    {
        private DiagramViewMode _viewMode = DiagramViewMode.CostPercent;
        public DiagramViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                _viewMode = value;
                OnPropertyChanged(nameof(ViewMode));
                OnPropertyChanged(nameof(PrimaryDisplayValue));
            }
        }

        private PlanColorMode _colorMode = PlanColorMode.TotalCost;
        public PlanColorMode ColorMode
        {
            get => _colorMode;
            set
            {
                _colorMode = value;
                OnPropertyChanged(nameof(ColorMode));
                OnPropertyChanged(nameof(ActivePercent));
                OnPropertyChanged(nameof(DynamicBackgroundBrush));
                OnPropertyChanged(nameof(DynamicBorderBrush));
                OnPropertyChanged(nameof(DynamicBorderThickness));
                OnPropertyChanged(nameof(PrimaryDisplayValue));
                OnPropertyChanged(nameof(CostBadgeBrush));
                OnPropertyChanged(nameof(CostBadgeForeground));
            }
        }

        public double ActivePercent
        {
            get
            {
                return ColorMode switch
                {
                    PlanColorMode.TotalCost => CostPercent,
                    PlanColorMode.CpuCost => CpuPercent,
                    PlanColorMode.IoCost => IoPercent,
                    _ => CostPercent
                };
            }
        }

        public double AvgRowSizeNum { get; set; }
        public double EstimatedCPUCostNum { get; set; }
        public double EstimatedIOCostNum { get; set; }
        public double CpuPercent { get; set; }
        public double IoPercent { get; set; }

        public string NodeId { get; set; } = "?";
        public string PhysicalOp { get; set; } = "Unknown";
        public string LogicalOp { get; set; } = "";
        public double Cost { get; set; }
        public double OwnCost { get; set; }
        public double SubtreeCost { get; set; }
        public int CostPercent { get; set; }
        public string EstRows { get; set; } = "0";
        public double EstRowsNum { get; set; }
        public string ActualRows { get; set; } = "";
        public double ActualRowsNum { get; set; }
        public string ObjectDetails { get; set; } = "";

        public double X { get; set; }
        public double Y { get; set; }
        public double SubtreeWidth { get; set; }
        public Geometry? IconGeometry { get; set; }
        public Brush? IconBrush { get; set; }

        public string OperatorType { get; set; } = "Other";
        public bool IsParallel { get; set; }
        public string Warnings { get; set; } = "";
        private static readonly Core.Services.PlanGraphCostVisualService CostVisualService = new();
        private static readonly Core.Services.PlanGraphNodeDisplayService NodeDisplayService = new();
        private static readonly Core.Services.PlanGraphOperatorVisualService OperatorVisualService = new();
        private static readonly Core.Services.PlanGraphRowSkewService RowSkewService = new();

        private SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion? _associatedSuggestion;
        public SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion? AssociatedSuggestion
        {
            get => _associatedSuggestion;
            set
            {
                if (_associatedSuggestion != value)
                {
                    _associatedSuggestion = value;
                    OnPropertyChanged(nameof(AssociatedSuggestion));
                    OnPropertyChanged(nameof(HasIndexRecommendation));
                    OnPropertyChanged(nameof(MissingIndexOverlayVisible));
                    OnPropertyChanged(nameof(MissingIndexTooltip));
                }
            }
        }

        public bool HasIndexRecommendation => _associatedSuggestion != null;
        public string MissingIndexOverlayVisible => HasIndexRecommendation ? "Visible" : "Collapsed";
        public string MissingIndexTooltip => _associatedSuggestion != null
            ? $"包含索引推荐:\n{_associatedSuggestion.CreateIndexStatement}\n\n点击在此表上打开索引优化沙盒模拟。"
            : string.Empty;

        public XElement? RawElement { get; set; }

        public double ActualRecost { get; set; }
        public string ExecutionMode { get; set; } = "Row";
        public string ActualRowsRead { get; set; } = "";
        public string EstimatedRowsToBeRead { get; set; } = "";
        public string EstimatedIOCost { get; set; } = "";
        public string EstimatedCPUCost { get; set; } = "";
        public string ActualExecutions { get; set; } = "";
        public string EstimatedExecutions { get; set; } = "";
        public string EstimatedOperatorCost { get; set; } = "";
        public string EstimatedSubtreeCostStr { get; set; } = "";
        public string EstimatedRowSize { get; set; } = "";
        public string ActualDataSize { get; set; } = "";
        public string EstimatedDataSize { get; set; } = "";
        public string ActualRebinds { get; set; } = "0";
        public string ActualRewinds { get; set; } = "0";
        public string Ordered { get; set; } = "False";
        public string DatabaseName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string IndexName { get; set; } = "";
        public string SeekPredicates { get; set; } = "";
        public string Predicate { get; set; } = "";
        public string OutputList { get; set; } = "";

        public string Partitioned { get; set; } = "False";
        public string PartitionCount { get; set; } = "";
        public string PartitionRange { get; set; } = "";

        public bool IsFullPartitionScan => NodeDisplayService.IsFullPartitionScan(Partitioned, PartitionCount, PartitionRange);
        public string PartitionRangeColor => NodeDisplayService.GetPartitionRangeColor(Partitioned, PartitionCount, PartitionRange);
        public string PartitionLabelColor => NodeDisplayService.GetPartitionLabelColor(Partitioned, PartitionCount, PartitionRange);

        public string HasSeekPredicates => NodeDisplayService.GetTextVisibility(SeekPredicates);
        public string HasPredicate => NodeDisplayService.GetTextVisibility(Predicate);
        public string HasOutputList => NodeDisplayService.GetTextVisibility(OutputList);
        public string HasPartitionInfo => NodeDisplayService.GetPartitionInfoVisibility(Partitioned);

        public string NodeSeverity { get; set; } = "Info"; // Info, Warning, Critical
        public string NodeSeverityColor => NodeDisplayService.GetNodeSeverityColor(NodeSeverity);
        public string NodeSeverityBorderThickness => NodeDisplayService.GetNodeSeverityBorderThickness(NodeSeverity);

        private bool _isCollapsed;
        public bool IsCollapsed
        {
            get => _isCollapsed;
            set
            {
                _isCollapsed = value;
                OnPropertyChanged(nameof(IsCollapsed));
                OnPropertyChanged(nameof(CollapseButtonText));
            }
        }

        public string CollapseButtonText => IsCollapsed ? "+" : "-";

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }

        private bool _hasChildren;
        public bool HasChildren
        {
            get => _hasChildren;
            set
            {
                _hasChildren = value;
                OnPropertyChanged(nameof(HasChildren));
                OnPropertyChanged(nameof(CollapseButtonVisibility));
            }
        }

        public string CollapseButtonVisibility => NodeDisplayService.GetBooleanVisibility(HasChildren);

        private Point _location;
        public Point Location { get => _location; set { _location = value; OnPropertyChanged(nameof(Location)); } }

        // === 模板友好计算属性 (Plan Explorer 视觉) ===
        public string LogicalOpSuffix => string.IsNullOrEmpty(LogicalOp) || LogicalOp == PhysicalOp ? "" : $"({LogicalOp})";

        public string PrimaryDisplayValue
        {
            get
            {
                return ViewMode switch
                {
                    DiagramViewMode.CostPercent => ColorMode switch
                    {
                        PlanColorMode.TotalCost => $"Cost: {CostPercent}%",
                        PlanColorMode.CpuCost => $"CPU: {CpuPercent:F1}%",
                        PlanColorMode.IoCost => $"I/O: {IoPercent:F1}%",
                        _ => $"Cost: {CostPercent}%"
                    },
                    DiagramViewMode.CpuIo => $"C: {EstimatedCPUCost}\nI: {EstimatedIOCost}",
                    DiagramViewMode.Rows => $"R: {(ActualRowsNum > 0 ? ActualRows : EstRows)}",
                    _ => $"{CostPercent}%"
                };
            }
        }

        public string ActualRowsDisplay => string.IsNullOrEmpty(ActualRows) ? "N/A" : ActualRows;

        public Brush DynamicBackgroundBrush
        {
            get
            {
                Core.Services.PlanGraphCostVisualStyle style =
                    CostVisualService.GetStyle(ActivePercent);
                return new LinearGradientBrush(
                    CreateColor(style.BackgroundTopColorHex),
                    CreateColor(style.BackgroundBottomColorHex),
                    90.0);
            }
        }

        public Brush DynamicBorderBrush
        {
            get
            {
                Core.Services.PlanGraphCostVisualStyle style =
                    CostVisualService.GetStyle(ActivePercent);
                return CreateBrush(style.BorderColorHex);
            }
        }

        public Thickness DynamicBorderThickness =>
            new(CostVisualService.GetStyle(ActivePercent).BorderThickness);

        private static Color CreateColor(string colorHex)
        {
            object? converted = ColorConverter.ConvertFromString(colorHex);
            return converted is Color color
                ? color
                : Colors.Transparent;
        }

        private static Brush CreateBrush(string colorHex)
            => new SolidColorBrush(CreateColor(colorHex));

        public Brush AccentBrush =>
            CreateBrush(OperatorVisualService.GetStyle(OperatorType).AccentColorHex);

        public string OperatorGeometry =>
            OperatorVisualService.GetStyle(OperatorType).GeometryData;

        public Brush CostBadgeBrush =>
            CreateBrush(CostVisualService.GetStyle(ActivePercent).BadgeBackgroundColorHex);

        public Brush CostBadgeForeground =>
            CreateBrush(CostVisualService.GetStyle(ActivePercent).BadgeForegroundColorHex);

        public Brush ActualRowsBrush
        {
            get
            {
                Core.Services.PlanGraphRowSkewResult result =
                    RowSkewService.Analyze(ActualRowsNum, EstRowsNum);

                return result.BrushKey switch
                {
                    Core.Services.PlanGraphRowSkewBrushKey.DarkRed => Brushes.DarkRed,
                    Core.Services.PlanGraphRowSkewBrushKey.DarkOrange => Brushes.DarkOrange,
                    Core.Services.PlanGraphRowSkewBrushKey.HealthyGreen => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                    _ => Brushes.DimGray
                };
            }
        }

        public string SkewWarning =>
            RowSkewService.Analyze(ActualRowsNum, EstRowsNum).Warning;

        public string HasObjectDetails => NodeDisplayService.GetTextVisibility(ObjectDetails);
        public string IsParallelVisible => NodeDisplayService.GetBooleanVisibility(IsParallel);
        public string HasWarningVisible => NodeDisplayService.GetTextVisibility(Warnings);
        public string HasExtraInfo => NodeDisplayService.GetExtraInfoVisibility(IsParallel, Warnings);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ConnectionViewModel : INotifyPropertyChanged
    {
        private PlanNodeViewModel? _source;
        private PlanNodeViewModel? _target;
        private static readonly Core.Services.PlanGraphConnectionDisplayService ConnectionDisplayService = new();
        private static readonly Core.Services.PlanGraphConnectionGeometryService ConnectionGeometryService = new();

        private static readonly Brush DefaultBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C));
        private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
        private static readonly Brush OrangeBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00));
        private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x8E, 0x3C));

        static ConnectionViewModel()
        {
            DefaultBrush.Freeze();
            RedBrush.Freeze();
            OrangeBrush.Freeze();
            GreenBrush.Freeze();
        }

        private static Core.Services.PlanGraphConnectionNodeInfo? ToConnectionNodeInfo(
            PlanNodeViewModel? node)
        {
            return node == null
                ? null
                : new Core.Services.PlanGraphConnectionNodeInfo(
                    node.PhysicalOp,
                    node.EstRowsNum,
                    node.ActualRows,
                    node.ActualRowsNum,
                    node.AvgRowSizeNum);
        }

        private static Core.Services.PlanGraphConnectionMetricKind ToMetricKind(
            LinkMetricMode metricMode)
        {
            return metricMode == LinkMetricMode.DataSize
                ? Core.Services.PlanGraphConnectionMetricKind.DataSize
                : Core.Services.PlanGraphConnectionMetricKind.RowCount;
        }

        private static Core.Services.PlanGraphConnectionGeometryNode? ToGeometryNode(
            PlanNodeViewModel? node)
        {
            return node == null
                ? null
                : new Core.Services.PlanGraphConnectionGeometryNode(
                    node.Location.X,
                    node.Location.Y);
        }

        private static Core.Services.PlanGraphConnectionLayout ToConnectionLayout(
            PlanLayoutMode layoutMode)
        {
            return layoutMode == PlanLayoutMode.Horizontal
                ? Core.Services.PlanGraphConnectionLayout.Horizontal
                : Core.Services.PlanGraphConnectionLayout.Vertical;
        }

        private static Point ToPoint(
            Core.Services.PlanGraphConnectionPoint point)
        {
            return new Point(point.X, point.Y);
        }

        private static Core.Services.PlanGraphConnectionPoint ToConnectionPoint(
            Point point)
        {
            return new Core.Services.PlanGraphConnectionPoint(point.X, point.Y);
        }

        private static Brush ToStrokeBrush(
            Core.Services.PlanGraphConnectionStrokeKey strokeKey)
        {
            return strokeKey switch
            {
                Core.Services.PlanGraphConnectionStrokeKey.Red => RedBrush,
                Core.Services.PlanGraphConnectionStrokeKey.Orange => OrangeBrush,
                Core.Services.PlanGraphConnectionStrokeKey.Green => GreenBrush,
                _ => DefaultBrush
            };
        }

        private PlanLayoutMode _layoutMode = PlanLayoutMode.Horizontal;

        public double ArrowAngle
        {
            get
            {
                return ConnectionGeometryService.GetArrowAngle(
                    ToConnectionLayout(LayoutMode));
            }
        }

        public PlanLayoutMode LayoutMode
        {
            get => _layoutMode;
            set
            {
                _layoutMode = value;
                OnPropertyChanged(nameof(LayoutMode));
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
            }
        }

        private LinkMetricMode _currentLinkMetric = LinkMetricMode.RowCount;
        public LinkMetricMode CurrentLinkMetric
        {
            get => _currentLinkMetric;
            set
            {
                _currentLinkMetric = value;
                OnPropertyChanged(nameof(CurrentLinkMetric));
                OnPropertyChanged(nameof(ThicknessValue));
                OnPropertyChanged(nameof(LabelText));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }

        public PlanNodeViewModel? Source
        {
            get => _source;
            set
            {
                if (_source != null)
                    _source.PropertyChanged -= OnSourcePropertyChanged;
                _source = value;
                if (_source != null)
                    _source.PropertyChanged += OnSourcePropertyChanged;
                OnPropertyChanged(nameof(Source));
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
                OnPropertyChanged(nameof(RowsCount));
                OnPropertyChanged(nameof(DataSizeVal));
                OnPropertyChanged(nameof(StrokeBrush));
                OnPropertyChanged(nameof(ToolTipText));
                OnPropertyChanged(nameof(LabelText));
                OnPropertyChanged(nameof(ThicknessValue));
            }
        }

        public PlanNodeViewModel? Target
        {
            get => _target;
            set
            {
                if (_target != null)
                    _target.PropertyChanged -= OnTargetPropertyChanged;
                _target = value;
                if (_target != null)
                    _target.PropertyChanged += OnTargetPropertyChanged;
                OnPropertyChanged(nameof(Target));
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
                OnPropertyChanged(nameof(RowsCount));
                OnPropertyChanged(nameof(DataSizeVal));
                OnPropertyChanged(nameof(StrokeBrush));
                OnPropertyChanged(nameof(ToolTipText));
                OnPropertyChanged(nameof(LabelText));
                OnPropertyChanged(nameof(ThicknessValue));
            }
        }

        public double RowsCount =>
            ConnectionDisplayService.CalculateRowsCount(
                ToConnectionNodeInfo(Source));

        public double DataSizeVal =>
            ConnectionDisplayService.CalculateDataSize(
                ToConnectionNodeInfo(Source));

        public double ThicknessValue
        {
            get
            {
                double val = ConnectionDisplayService.GetMetricValue(
                    ToMetricKind(CurrentLinkMetric),
                    ToConnectionNodeInfo(Source));

                return Core.Services.PlanGraphMetricService.CalculateLinkThickness(val);
            }
        }

        public Point SourceLocation
        {
            get
            {
                return ToPoint(ConnectionGeometryService.CalculateSourceLocation(
                    ToGeometryNode(Source),
                    ToGeometryNode(Target),
                    ToConnectionLayout(LayoutMode)));
            }
        }

        public Point TargetLocation
        {
            get
            {
                return ToPoint(ConnectionGeometryService.CalculateTargetLocation(
                    ToGeometryNode(Source),
                    ToGeometryNode(Target),
                    ToConnectionLayout(LayoutMode)));
            }
        }

        private Core.Services.PlanGraphConnectionPoint LabelLocation =>
            ConnectionGeometryService.CalculateLabelLocation(
                ToConnectionPoint(SourceLocation),
                ToConnectionPoint(TargetLocation),
                LabelText);

        public double MidpointX => LabelLocation.X;
        public double MidpointY => LabelLocation.Y;

        private bool _isHighlighted = true;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                _isHighlighted = value;
                OnPropertyChanged(nameof(IsHighlighted));
                OnPropertyChanged(nameof(Opacity));
            }
        }

        public double Opacity => IsHighlighted ? 1.0 : 0.35;

        public Brush StrokeBrush
        {
            get
            {
                Core.Services.PlanGraphConnectionStrokeKey strokeKey =
                    ConnectionDisplayService.GetStrokeKey(
                        ToConnectionNodeInfo(Source));
                return ToStrokeBrush(strokeKey);
            }
        }

        public string LabelText
        {
            get
            {
                return ConnectionDisplayService.BuildLabel(
                    ToMetricKind(CurrentLinkMetric),
                    ToConnectionNodeInfo(Source));
            }
        }

        public string ToolTipText
        {
            get
            {
                return ConnectionDisplayService.BuildToolTip(
                    ToConnectionNodeInfo(Source),
                    Target?.PhysicalOp);
            }
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanNodeViewModel.Location))
            {
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
            }
        }

        private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanNodeViewModel.Location))
            {
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ConnectionThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Core.Services.PlanGraphMetricService.CalculateLegacyConverterThickness(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        public RelayCommand(Action<T> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            if (parameter is T val)
            {
                _execute(val);
            }
            else if (parameter == null)
            {
                _execute(default!);
            }
        }
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
