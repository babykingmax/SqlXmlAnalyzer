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
        private static readonly Core.Rules.RuleEngine _ruleEngine = new Core.Rules.RuleEngine();
        private static readonly Core.Services.PlanGraphRelOpDetailsService RelOpDetailsService = new();
        private static readonly Core.Services.PlanGraphRuntimeCountersService RuntimeCountersService = new();

        static PlanGraphControl()
        {
            _ruleEngine.RegisterDefaultRules();
        }

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

            // 关联 Missing Indexes 推荐到 Operator 节点
            var missingIndexes = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, ns);
            foreach (var vm in allNodes)
            {
                if (!string.IsNullOrEmpty(vm.TableName))
                {
                    string cleanVmTable = vm.TableName.Trim('[', ']');
                    var match = missingIndexes.FirstOrDefault(mi =>
                        string.Equals(mi.Table.Trim('[', ']'), cleanVmTable, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        vm.AssociatedSuggestion = match;
                    }
                }
            }

            // 计算真实的 own_cost 并从 subtree_cost 中减去子节点的 subtree_cost
            foreach (var relOp in relOps)
            {
                var vm = nodeMap[relOp];
                var childRelOps = PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns).ToList();
                vm.HasChildren = childRelOps.Count > 0;
                double childrenSubtreeCost = childRelOps.Sum(c =>
                {
                    if (nodeMap.TryGetValue(c, out var cvm)) return cvm.SubtreeCost;
                    return safeFloat(c.Attribute("EstimatedTotalSubtreeCost")?.Value);
                });

                vm.OwnCost = Math.Max(0.0, vm.SubtreeCost - childrenSubtreeCost);
                vm.Cost = vm.OwnCost; // 让 Cost 代表 OwnCost

                if (vm.EstRowsNum > 0 && !string.IsNullOrEmpty(vm.ActualRows))
                {
                    vm.ActualRecost = vm.OwnCost * (vm.ActualRowsNum / vm.EstRowsNum);
                    if (double.IsInfinity(vm.ActualRecost) || double.IsNaN(vm.ActualRecost))
                        vm.ActualRecost = vm.OwnCost;
                }
                else
                {
                    vm.ActualRecost = vm.OwnCost;
                }
            }

            // 计算和分配自身成本百分比 CostPercent, CpuPercent, IoPercent
            double maxSubtreeCost = allNodes.Count > 0 ? allNodes.Max(n => n.SubtreeCost) : 0.0;
            if (maxSubtreeCost <= 0) maxSubtreeCost = 1.0;

            double maxCpuCost = allNodes.Count > 0 ? allNodes.Max(n => n.EstimatedCPUCostNum) : 0.0;
            if (maxCpuCost <= 0) maxCpuCost = 1.0;

            double maxIoCost = allNodes.Count > 0 ? allNodes.Max(n => n.EstimatedIOCostNum) : 0.0;
            if (maxIoCost <= 0) maxIoCost = 1.0;

            foreach (var vm in allNodes)
            {
                double pct = (vm.OwnCost / maxSubtreeCost) * 100.0;
                vm.CostPercent = (int)Math.Min(100, Math.Max(0, Math.Round(pct)));

                double cpuPct = (vm.EstimatedCPUCostNum / maxCpuCost) * 100.0;
                vm.CpuPercent = Math.Min(100.0, Math.Max(0.0, cpuPct));

                double ioPct = (vm.EstimatedIOCostNum / maxIoCost) * 100.0;
                vm.IoPercent = Math.Min(100.0, Math.Max(0.0, ioPct));

                vm.ViewMode = initialView;
                vm.ColorMode = initialColor;
            }

            // 2. 简单分层初始布局 (类似 Plan Explorer 水平/垂直流)
            ApplyLayeredLayout(nodeMap, relOps, ns);

            // 3. 构建父子连接 (子 -> 父，数据流向根)
            foreach (var relOp in relOps)
            {
                var parentVm = nodeMap[relOp];
                foreach (var child in PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns))
                {
                    if (nodeMap.TryGetValue(child, out var childVm))
                    {
                        Connections.Add(new ConnectionViewModel
                        {
                            Source = childVm,
                            Target = parentVm,
                            LayoutMode = initialLayout,
                            CurrentLinkMetric = initialLinkMetric
                        });
                    }
                }
            }

            // 添加到集合 (Nodify 会自动响应)
            _masterNodes = allNodes;
            _masterConnections = Connections.ToList();
            foreach (var n in allNodes) Nodes.Add(n);

            // 默认选中根节点 (最高成本或第一个)
            SelectedNode = allNodes.OrderByDescending(n => n.CostPercent).FirstOrDefault() ?? allNodes.FirstOrDefault();
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
            string nodeId = relOp.Attribute("NodeId")?.Value ?? "?";
            string physical = relOp.Attribute("PhysicalOp")?.Value ?? relOp.Attribute("LogicalOp")?.Value ?? "Unknown";
            string logical = relOp.Attribute("LogicalOp")?.Value ?? "Unknown";

            double estRows = safeFloat(relOp.Attribute("EstimateRows")?.Value);
            double estRowsRead = safeFloat(relOp.Attribute("EstimatedRowsRead")?.Value, estRows);
            double subtreeCost = safeFloat(relOp.Attribute("EstimatedTotalSubtreeCost")?.Value);

            string estIoCost = relOp.Attribute("EstimateIO")?.Value ?? "0";
            string estCpuCost = relOp.Attribute("EstimateCPU")?.Value ?? "0";
            string estExecs = relOp.Attribute("EstimateRebinds") != null ?
                (safeFloat(relOp.Attribute("EstimateRebinds")?.Value) + safeFloat(relOp.Attribute("EstimateRewinds")?.Value) + 1.0).ToString("0.0") : "1.0";
            string estRowSize = relOp.Attribute("AvgRowSize")?.Value ?? "0";

            Core.Services.PlanGraphRuntimeCountersResult runtimeCounters =
                RuntimeCountersService.Parse(relOp, ns);
            double actualRows = runtimeCounters.ActualRows;
            double actualRowsRead = runtimeCounters.ActualRowsRead;
            double actualExecutions = runtimeCounters.ActualExecutions;
            bool hasActual = runtimeCounters.HasActual;
            bool hasActualRead = runtimeCounters.HasActualRead;
            double actualRebinds = runtimeCounters.ActualRebinds;
            double actualRewinds = runtimeCounters.ActualRewinds;
            bool isSkewed = runtimeCounters.IsThreadDataSkewed;

            Core.Services.PlanGraphRelOpDetails relOpDetails =
                RelOpDetailsService.Parse(relOp, ns, physical);
            string residualPredicate = string.Join(" AND ", relOpDetails.Predicates);
            string seekPredicate = string.Join(" AND ", relOpDetails.SeekPredicates);

            double dEstRowSize = safeFloat(estRowSize);
            double dEstCpu = safeFloat(estCpuCost);
            double dEstIo = safeFloat(estIoCost);
            double dEstDataSizeMB = (estRows * dEstRowSize) / (1024.0 * 1024.0);
            double dActDataSizeMB = hasActual ? ((actualRows * dEstRowSize) / (1024.0 * 1024.0)) : 0.0;
            string sEstDataSize = dEstDataSizeMB < 1.0 ? $"{(dEstDataSizeMB * 1024):F0} KB" : $"{dEstDataSizeMB:F0} MB";
            string sActDataSize = dActDataSizeMB < 1.0 ? $"{(dActDataSizeMB * 1024):F0} KB" : $"{dActDataSizeMB:F0} MB";

            // Residual Predicate Warning
            bool hasResidualStr = physical.Contains("Seek") && !string.IsNullOrEmpty(seekPredicate) && !string.IsNullOrEmpty(residualPredicate);
            bool hasResidualWarning = false;

            // Residual I/O Automatic Detection and Warning (新增高级诊断)
            bool hasResidualPredicate = !string.IsNullOrEmpty(residualPredicate) || relOp.Elements(ns + "Predicate").Any(p => p.Parent?.Name != ns + "SeekPredicate");
            bool hasResidualIOWarning = false;
            string residualIOWarningDetails = "";

            if (hasResidualPredicate && hasActual && hasActualRead)
            {
                if (actualRowsRead > ResidualIOMinRowsRead && actualRowsRead > actualRows * ResidualIOThreshold)
                {
                    hasResidualIOWarning = true;
                    double ratio = actualRows > 0 ? actualRowsRead / actualRows : actualRowsRead;
                    residualIOWarningDetails =
                        $"**残差 I/O 警告**\n" +
                        $"操作符: {physical}\n" +
                        $"实际读取行数: {actualRowsRead:N0}\n" +
                        $"实际返回行数: {actualRows:N0}\n" +
                        $"读取/返回比: {ratio:F1} : 1\n" +
                        $"说明: 该操作符因残差谓词过滤了大部分读取的行，造成大量额外 I/O。\n" +
                        $"建议: 考虑将谓词改为索引列能直接查找的条件，或添加包含列的覆盖索引。";
                }
            }

            if (!hasResidualIOWarning && hasResidualStr)
            {
                if (hasActual && hasActualRead)
                {
                    if (actualRowsRead > actualRows * 1.2 && (actualRowsRead - actualRows) > 100)
                        hasResidualWarning = true;
                }
                else
                {
                    hasResidualWarning = true;
                }
            }

            // Warnings parsing
            var warningsList = new List<string>();

            // 1. RelOp Warnings
            var warningsEl = relOp.Element(ns + "Warnings");
            if (warningsEl != null)
            {
                foreach (var warnNode in warningsEl.Elements())
                {
                    if (warnNode == null) continue;
                    string warnText = $"⚠ 操作符警告: {warnNode.Name.LocalName}";
                    if (warnNode.Name.LocalName == "PlanAffectingConvert")
                    {
                        var expr = warnNode.Attribute("Expression")?.Value;
                        if (!string.IsNullOrEmpty(expr)) warnText += $"\n   [转换表达式]: {expr}";
                    }
                    else if (warnNode.Name.LocalName == "HashWarning" || warnNode.Name.LocalName == "SortWarning")
                    {
                        var memWarn = warnNode.Attribute("HashWarningDetail")?.Value ?? warnNode.Attribute("SortWarningDetail")?.Value;
                        if (!string.IsNullOrEmpty(memWarn)) warnText += $" ({memWarn})";
                    }
                    warningsList.Add(warnText);
                }
            }

            // 2. CONVERT_IMPLICIT check
            var implicitConverts = relOp.Descendants(ns + "ScalarOperator")
                .Where(op => op.Attribute("ScalarString")?.Value?.Contains("CONVERT_IMPLICIT") == true)
                .Select(op => op.Attribute("ScalarString")?.Value)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (implicitConverts.Count > 0)
            {
                warningsList.Add($"隐式类型转换 (CONVERT_IMPLICIT):\n   " + string.Join("\n   ", implicitConverts));
            }

            // 3. Global Memory Warnings (show on Root node)
            if (nodeId == "0" || nodeId == "1") // 根节点通常显示整体内存信息
            {
                var memGrantInfo = relOp.Document?.Descendants(ns + "MemoryGrantInfo").FirstOrDefault();
                if (memGrantInfo != null)
                {
                    double granted = safeFloat(memGrantInfo.Attribute("GrantedMemory")?.Value);
                    double used = safeFloat(memGrantInfo.Attribute("MaxUsedMemory")?.Value);
                    if (granted > 10240 && used > 0 && (used / granted) < 0.1)
                        warningsList.Add($"内存预估过度 (申请 {granted / 1024.0:F1}MB, 仅用 {used / 1024.0:F1}MB)");
                    else if (granted > 0 && used > granted)
                        warningsList.Add($"内存不足溢出落盘 (申请 {granted / 1024.0:F1}MB, 实际需 {used / 1024.0:F1}MB)");
                }

                var globalWarnings = relOp.Document?.Descendants(ns + "Warnings").FirstOrDefault();
                if (globalWarnings != null)
                {
                    var memWarn = globalWarnings.Element(ns + "MemoryGrantWarning");
                    if (memWarn != null)
                    {
                        string type = memWarn.Attribute("GrantWarningKind")?.Value ?? "";
                        warningsList.Add($"内存分配警告: {type}");
                    }
                }
            }

            if (isSkewed)
                warningsList.Add("线程数据倾斜 (Thread Data Skew)");

            if (hasResidualIOWarning)
                warningsList.Add(residualIOWarningDetails);
            else if (hasResidualWarning)
                warningsList.Add("残差谓词寻址 (Residual Predicate)");

            // ===== Rule Engine Execution =====
            var ruleResults = _ruleEngine.AnalyzeNode(relOp, ns);
            string highestSeverity = "Info";
            foreach (var r in ruleResults)
            {
                warningsList.Add($"[{r.Severity}] {r.Title}: {r.Message}");
                if (r.Severity == "Critical") highestSeverity = "Critical";
                else if (r.Severity == "Warning" && highestSeverity != "Critical") highestSeverity = "Warning";
            }
            // =================================

            string warningsStr = string.Join("\n• ", warningsList);
            if (warningsList.Count > 0) warningsStr = "• " + warningsStr;

            // Parallelism
            bool isParallel = relOp.Attribute("Parallel")?.Value == "1" ||
                              relOp.Descendants(ns + "ThreadStat").Any() ||
                              physical.Contains("Parallelism");

            // Calculate own cost (will subtract children subtree cost later in LoadFromExecutionPlan)
            double ownCost = subtreeCost; // Temporary, subtracted later

            double actualRecost = ownCost;
            if (hasActual && estRows > 0)
            {
                actualRecost = ownCost * (actualRows / estRows);
                if (double.IsInfinity(actualRecost) || double.IsNaN(actualRecost))
                    actualRecost = ownCost;
            }

            var vm = new PlanNodeViewModel
            {
                RawElement = relOp,
                NodeId = nodeId,
                PhysicalOp = physical,
                LogicalOp = logical,
                ExecutionMode = relOp.Attribute("NodeId") != null ? "Row" : "Row",
                Cost = ownCost, // Represents OwnCost
                OwnCost = ownCost,
                ActualRecost = actualRecost,
                SubtreeCost = subtreeCost,
                CostPercent = 1, // Will update globally
                EstRows = FormatNumber(estRows),
                EstRowsNum = estRows,
                EstimatedRowsToBeRead = FormatNumber(estRowsRead),
                EstimatedCPUCostNum = dEstCpu,
                EstimatedIOCostNum = dEstIo,
                AvgRowSizeNum = dEstRowSize,
                EstimatedIOCost = estIoCost,
                EstimatedCPUCost = estCpuCost,
                EstimatedExecutions = estExecs,
                ActualExecutions = hasActual ? actualExecutions.ToString("F0") : "",
                ActualRows = hasActual ? actualRows.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "",
                ActualRowsRead = hasActual && hasActualRead ? actualRowsRead.ToString("N0") : "",
                ActualRowsNum = actualRows,
                EstimatedOperatorCost = ownCost.ToString("0.0000000"),
                EstimatedSubtreeCostStr = subtreeCost.ToString("0.0000000"),
                EstimatedRowSize = dEstRowSize.ToString("0") + " B",
                EstimatedDataSize = sEstDataSize,
                ActualDataSize = hasActual ? sActDataSize : "",
                ActualRebinds = hasActual ? actualRebinds.ToString() : "",
                ActualRewinds = hasActual ? actualRewinds.ToString() : "",
                Ordered = relOp.Attribute("LogicalOp")?.Value?.Contains("Sort") == true ? "True" : "False",
                DatabaseName = relOpDetails.DatabaseName,
                TableName = relOpDetails.TableName,
                IndexName = relOpDetails.IndexName,
                SeekPredicates = string.Join("\n", relOpDetails.SeekPredicates),
                Predicate = string.Join("\n", relOpDetails.Predicates),
                OutputList = string.Join(", ", relOpDetails.OutputColumns),
                ObjectDetails = relOpDetails.ObjectDetails,
                Partitioned = relOpDetails.IsPartitioned ? "True" : "False",
                PartitionCount = relOpDetails.PartitionCount,
                PartitionRange = relOpDetails.PartitionRange,
                IsParallel = isParallel,
                Warnings = warningsStr,
                NodeSeverity = highestSeverity,
                Location = new Point(50, 50)
            };

            var iconInfo = PhysicalOpToIconMapper.Map(physical);
            vm.IconGeometry = iconInfo.Geometry;
            vm.IconBrush = iconInfo.Brush;

            var operatorTypeService = new Core.Services.PlanGraphOperatorTypeService();
            vm.OperatorType = operatorTypeService.DetectOperatorType(physical, logical);

            return vm;
        }

        private void ApplyLayeredLayout(
            Dictionary<XElement, PlanNodeViewModel> nodeMap,
            List<XElement> allRelOps,
            XNamespace ns)
        {
            var collapsedRelOps = nodeMap
                .Where(pair => pair.Value.IsCollapsed)
                .Select(pair => pair.Key)
                .ToHashSet();
            var layoutService = new Core.Services.PlanGraphLayoutService();
            IReadOnlyList<Core.Services.PlanGraphLayoutPosition> positions =
                layoutService.CalculateLayout(
                    allRelOps,
                    ns,
                    collapsedRelOps,
                    ToGraphLayoutDirection(LayoutMode));

            foreach (Core.Services.PlanGraphLayoutPosition position in positions)
            {
                if (nodeMap.TryGetValue(position.Element, out PlanNodeViewModel? vm))
                {
                    vm.SubtreeWidth = position.SubtreeWidth;
                    vm.Location = new Point(position.X, position.Y);
                }
            }
        }

        private static Core.Services.PlanGraphLayoutDirection ToGraphLayoutDirection(
            PlanLayoutMode layoutMode)
        {
            return layoutMode == PlanLayoutMode.Horizontal
                ? Core.Services.PlanGraphLayoutDirection.Horizontal
                : Core.Services.PlanGraphLayoutDirection.Vertical;
        }

        internal static string FormatNumber(double n)
        {
            return Core.Services.PlanGraphMetricService.FormatNumber(n);
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
            foreach (var n in _masterNodes)
            {
                n.IsCollapsed = false;
            }
            UpdateGraphVisibility();
            ReapplyLayout();
        }

        private void SmartCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            var collapseNodes = new List<Core.Services.PlanGraphSmartCollapseNode>();
            foreach (var node in _masterNodes)
            {
                node.IsCollapsed = false; // 先全部展开
                if (node.RawElement != null)
                {
                    collapseNodes.Add(
                        new Core.Services.PlanGraphSmartCollapseNode(
                            node.RawElement,
                            node.HasChildren,
                            node.SubtreeCost,
                            node.NodeSeverity));
                }
            }

            var smartCollapseService = new Core.Services.PlanGraphSmartCollapseService();
            Core.Services.PlanGraphSmartCollapseResult result =
                smartCollapseService.CalculateCollapsedRelOps(collapseNodes);

            foreach (var n in _masterNodes)
            {
                if (n.RawElement != null)
                {
                    n.IsCollapsed = result.CollapsedRelOps.Contains(n.RawElement);
                }
            }

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
                    node.IsCollapsed = !node.IsCollapsed;

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

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in _masterNodes)
            {
                if (node.RawElement != null) nodeMap[node.RawElement] = node;
            }

            var collapsedRelOps = nodeMap
                .Where(pair => pair.Value.IsCollapsed)
                .Select(pair => pair.Key)
                .ToHashSet();
            var visibilityService = new Core.Services.PlanGraphVisibilityService();
            Core.Services.PlanGraphVisibilityResult visibility =
                visibilityService.CalculateVisibility(
                    relOps,
                    _currentNs,
                    collapsedRelOps);

            var visibleNodeVms = visibility.VisibleRelOps
                .Where(nodeMap.ContainsKey)
                .Select(relOp => nodeMap[relOp])
                .ToHashSet();
            var visibleConnVms = new HashSet<ConnectionViewModel>();

            foreach (Core.Services.PlanGraphVisibleConnection visibleConnection in visibility.VisibleConnections)
            {
                if (nodeMap.TryGetValue(visibleConnection.SourceRelOp, out PlanNodeViewModel? sourceVm)
                    && nodeMap.TryGetValue(visibleConnection.TargetRelOp, out PlanNodeViewModel? targetVm))
                {
                    ConnectionViewModel? connection = _masterConnections
                        .FirstOrDefault(c => c.Source == sourceVm && c.Target == targetVm);
                    if (connection != null)
                    {
                        visibleConnVms.Add(connection);
                    }
                }
            }

            // ==================================================
            // 完全基于 Nodify 推荐的设计模式：纯数据绑定
            // 不再动态向 ObservableCollection 中 Add/Remove 节点，
            // 而是所有节点都在集合中，仅通过更新 IsVisible 属性，
            // 让 ItemContainerStyle 中的 Visibility 绑定自动接管显示隐藏。
            // 这样彻底避免了虚拟化面板的集合变更 Bug 和动画丢失问题。
            // ==================================================

            foreach (var n in _masterNodes)
            {
                n.IsVisible = visibleNodeVms.Contains(n);
                if (!Nodes.Contains(n)) Nodes.Add(n); // 确保集合包含全部节点（通常初始化时已包含）
            }

            foreach (var c in _masterConnections)
            {
                c.IsVisible = visibleConnVms.Contains(c);
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

            ApplyLayeredLayout(nodeMap, relOps, _currentNs);

            foreach (var conn in _masterConnections)
            {
                conn.LayoutMode = LayoutMode;
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
