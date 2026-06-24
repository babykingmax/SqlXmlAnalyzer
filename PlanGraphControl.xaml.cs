using Nodify;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
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
            ApplyLayeredLayout(allNodes, nodeMap, relOps, ns);

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

            // Calculate actual information
            double actualRows = 0.0;
            double actualRowsRead = 0.0;
            double actualExecutions = 0.0;
            bool hasActual = false;
            bool hasActualRead = false;
            double actualRebinds = 0.0;
            double actualRewinds = 0.0;
            var threadRows = new Dictionary<string, double>();

            var runInfo = relOp.Element(ns + "RunTimeInformation");
            if (runInfo != null)
            {
                hasActual = true;
                foreach (var rt in runInfo.Elements(ns + "RunTimeCountersPerThread"))
                {
                    string threadId = rt.Attribute("Thread")?.Value ?? "0";
                    double rows = safeFloat(rt.Attribute("ActualRows")?.Value);
                    double rowsRead = rows;
                    if (rt.Attribute("ActualRowsRead") != null)
                    {
                        rowsRead = safeFloat(rt.Attribute("ActualRowsRead")?.Value);
                        hasActualRead = true;
                    }
                    double execs = safeFloat(rt.Attribute("ActualExecutions")?.Value, 1.0);

                    threadRows[threadId] = rows;
                    actualRows += rows;
                    actualRowsRead += rowsRead;
                    actualExecutions += execs;

                    actualRebinds += safeFloat(rt.Attribute("ActualRebinds")?.Value);
                    actualRewinds += safeFloat(rt.Attribute("ActualRewinds")?.Value);
                }
            }
            if (!hasActual) actualExecutions = 0.0;

            // Thread Data Skew Detection
            bool isSkewed = false;
            var workerThreads = threadRows.Where(kv => kv.Key != "0").Select(kv => kv.Value).ToList();
            if (workerThreads.Count > 1 && workerThreads.Sum() > 100)
            {
                double avgRows = workerThreads.Sum() / workerThreads.Count;
                if (workerThreads.Max() > avgRows * 2.0)
                {
                    isSkewed = true;
                }
            }

            // Object and Predicates Extract
            string objectDetails = "";
            string databaseName = "";
            string tableName = "";
            string indexName = "";
            var predsNormal = new List<string>();
            var predsSeek = new List<string>();
            var outputList = new List<string>();

            foreach (var child in relOp.Elements())
            {
                string tagLocal = child.Name.LocalName;
                if (tagLocal == "OutputList")
                {
                    foreach (var col in child.Descendants(ns + "ColumnReference"))
                    {
                        string cName = col.Attribute("Column")?.Value ?? "";
                        if (!string.IsNullOrEmpty(cName) && !outputList.Contains(cName))
                            outputList.Add(cName);
                    }
                }
                else if (tagLocal != "Warnings" && tagLocal != "RunTimeInformation" && tagLocal != "RelOp")
                {
                    var obj = child.Descendants(ns + "Object").FirstOrDefault();
                    if (obj != null)
                    {
                        databaseName = obj.Attribute("Database")?.Value?.TrimStart('[').TrimEnd(']') ?? "";
                        tableName = obj.Attribute("Table")?.Value?.TrimStart('[').TrimEnd(']') ?? "";
                        indexName = obj.Attribute("Index")?.Value?.TrimStart('[').TrimEnd(']') ?? "";
                        string alias = obj.Attribute("Alias")?.Value?.TrimStart('[').TrimEnd(']') ?? "";

                        if (!string.IsNullOrEmpty(tableName))
                        {
                            objectDetails = string.IsNullOrEmpty(indexName) ? $"[{tableName}]" : $"[{tableName}].[{indexName}]";
                            if (!string.IsNullOrEmpty(alias) && alias != tableName)
                                objectDetails += $" AS [{alias}]";
                        }
                    }

                    // Find scalar operators under Predicate
                    foreach (var pred in child.Descendants(ns + "Predicate"))
                    {
                        foreach (var op in pred.Descendants(ns + "ScalarOperator"))
                        {
                            string? s = op.Attribute("ScalarString")?.Value;
                            if (!string.IsNullOrEmpty(s) && !predsNormal.Contains(s))
                                predsNormal.Add(s);
                        }
                    }

                    // Find scalar operators under SeekPredicates or SeekPredicateNew
                    var seekPredsElements = child.Descendants(ns + "SeekPredicates")
                                                .Concat(child.Descendants(ns + "SeekPredicateNew"));
                    foreach (var seekPred in seekPredsElements)
                    {
                        foreach (var op in seekPred.Descendants(ns + "ScalarOperator"))
                        {
                            string? s = op.Attribute("ScalarString")?.Value;
                            if (!string.IsNullOrEmpty(s) && !predsSeek.Contains(s))
                                predsSeek.Add(s);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(objectDetails) && (physical.Contains("Scan") || physical.Contains("Seek")))
                objectDetails = "(堆表或堆索引)";

            string residualPredicate = string.Join(" AND ", predsNormal);
            string seekPredicate = string.Join(" AND ", predsSeek);

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

            var partAccessed = relOp.Descendants(ns + "PartitionsAccessed").FirstOrDefault();
            string partitionCount = "";
            string partitionRange = "";
            bool isPartitioned = relOp.DescendantsAndSelf().Any(e => e.Attribute("Partitioned")?.Value?.ToLower() == "true" || e.Attribute("Partitioned")?.Value == "1");

            if (partAccessed != null)
            {
                isPartitioned = true;
                partitionCount = partAccessed.Attribute("PartitionCount")?.Value ?? "";
                var pRange = partAccessed.Element(ns + "PartitionRange");
                if (pRange != null)
                {
                    string start = pRange.Attribute("Start")?.Value ?? "";
                    string end = pRange.Attribute("End")?.Value ?? "";
                    if (!string.IsNullOrEmpty(start) && !string.IsNullOrEmpty(end))
                        partitionRange = $"{start} - {end}";
                    else if (!string.IsNullOrEmpty(start))
                        partitionRange = start;
                }
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
                DatabaseName = databaseName,
                TableName = tableName,
                IndexName = indexName,
                SeekPredicates = string.Join("\n", predsSeek),
                Predicate = string.Join("\n", predsNormal),
                OutputList = string.Join(", ", outputList),
                ObjectDetails = objectDetails,
                Partitioned = isPartitioned ? "True" : "False",
                PartitionCount = partitionCount,
                PartitionRange = partitionRange,
                IsParallel = isParallel,
                Warnings = warningsStr,
                NodeSeverity = highestSeverity,
                Location = new Point(50, 50)
            };

            var iconInfo = PhysicalOpToIconMapper.Map(physical);
            vm.IconGeometry = iconInfo.Geometry;
            vm.IconBrush = iconInfo.Brush;

            vm.OperatorType = DetectOperatorType(physical, logical);

            return vm;
        }

        private string DetectOperatorType(string physical, string logical)
        {
            string p = (physical + " " + logical).ToLower();
            if (p.Contains("scan")) return "Scan";
            if (p.Contains("seek") || p.Contains("bookmark")) return "Seek";
            if (p.Contains("join") || p.Contains("hash") || p.Contains("merge") || p.Contains("nested")) return "Join";
            if (p.Contains("parallelism") || p.Contains("exchange") || p.Contains("distribute") || p.Contains("gather")) return "Parallelism";
            if (p.Contains("sort") || p.Contains("top")) return "Sort";
            if (p.Contains("spool") || p.Contains("table spool")) return "Spool";
            if (p.Contains("compute") || p.Contains("scalar") || p.Contains("assign")) return "Compute";
            return "Other";
        }

        private void ApplyLayeredLayout(List<PlanNodeViewModel> allNodes, Dictionary<XElement, PlanNodeViewModel> nodeMap,
                                        List<XElement> allRelOps, XNamespace ns)
        {
            var roots = allRelOps.Where(r => !allRelOps.Any(p => PlanDiagnosticAnalyzer.GetDirectChildRelOps(p, ns).Contains(r))).ToList();
            if (roots.Count == 0 && allRelOps.Count > 0)
            {
                roots.Add(allRelOps[0]);
            }

            var childrenMap = new Dictionary<XElement, List<XElement>>();
            foreach (var op in allRelOps)
            {
                childrenMap[op] = PlanDiagnosticAnalyzer.GetDirectChildRelOps(op, ns).ToList();
            }

            double CalculateSubtreeWidth(XElement node)
            {
                var vm = nodeMap[node];
                if (vm.IsCollapsed)
                {
                    vm.SubtreeWidth = 1;
                    return 1;
                }

                var children = childrenMap[node];
                if (children.Count == 0)
                {
                    vm.SubtreeWidth = 1;
                    return 1;
                }

                double totalWidth = 0;
                foreach (var child in children)
                {
                    totalWidth += CalculateSubtreeWidth(child);
                }
                vm.SubtreeWidth = Math.Max(1, totalWidth);
                return vm.SubtreeWidth;
            }

            double horizontalSpacing = 280;
            double verticalSpacing = 160;

            void SetNodePositions(XElement node, double startY, double depthX)
            {
                var vm = nodeMap[node];
                vm.Location = new Point(depthX, startY + (vm.SubtreeWidth - 1) * verticalSpacing / 2);

                if (vm.IsCollapsed) return;

                var children = childrenMap[node];
                double childStartY = startY;
                foreach (var child in children)
                {
                    SetNodePositions(child, childStartY, depthX + horizontalSpacing);
                    childStartY += nodeMap[child].SubtreeWidth * verticalSpacing;
                }
            }

            void SetNodePositionsVertical(XElement node, double startX, double depthY)
            {
                var vm = nodeMap[node];
                vm.Location = new Point(startX + (vm.SubtreeWidth - 1) * horizontalSpacing / 2, depthY);

                if (vm.IsCollapsed) return;

                var children = childrenMap[node];
                double childStartX = startX;
                foreach (var child in children)
                {
                    SetNodePositionsVertical(child, childStartX, depthY + verticalSpacing);
                    childStartX += nodeMap[child].SubtreeWidth * horizontalSpacing;
                }
            }

            if (LayoutMode == PlanLayoutMode.Horizontal)
            {
                double currentY = 50;
                foreach (var root in roots)
                {
                    CalculateSubtreeWidth(root);
                    SetNodePositions(root, currentY, 50);
                    currentY += nodeMap[root].SubtreeWidth * verticalSpacing + 50;
                }
            }
            else
            {
                double currentX = 50;
                foreach (var root in roots)
                {
                    CalculateSubtreeWidth(root);
                    SetNodePositionsVertical(root, currentX, 50);
                    currentX += nodeMap[root].SubtreeWidth * horizontalSpacing + 50;
                }
            }
        }

        internal static string FormatNumber(double n)
        {
            if (n >= 1_000_000) return (n / 1_000_000).ToString("0.0") + "M";
            if (n >= 10_000) return (n / 1000).ToString("0.0") + "K";
            if (n >= 1000) return (n / 1000).ToString("0") + "K";
            return n.ToString("N0");
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
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Node ID: {node.NodeId}");
                sb.AppendLine($"Physical Op: {node.PhysicalOp}");
                sb.AppendLine($"Logical Op: {node.LogicalOp}");
                sb.AppendLine($"Estimated Cost: {node.SubtreeCost} ({node.CostPercent:F1}%)");
                sb.AppendLine($"Estimated Rows: {node.EstRows}");
                sb.AppendLine($"Actual Rows: {node.ActualRows}");
                sb.AppendLine($"Estimated Data Size: {node.EstimatedDataSize}");

                if (!string.IsNullOrEmpty(node.ObjectDetails))
                    sb.AppendLine($"Object: {node.ObjectDetails}");
                if (!string.IsNullOrEmpty(node.OutputList))
                    sb.AppendLine($"Output List: {node.OutputList}");
                if (!string.IsNullOrEmpty(node.SeekPredicates))
                    sb.AppendLine($"Seek Predicates: {node.SeekPredicates}");
                if (!string.IsNullOrEmpty(node.Predicate))
                    sb.AppendLine($"Predicate: {node.Predicate}");
                if (!string.IsNullOrEmpty(node.Warnings))
                    sb.AppendLine($"Warnings: {node.Warnings}");

                System.Windows.Clipboard.SetText(sb.ToString());

                ToastPopup.IsOpen = true;
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => ToastPopup.IsOpen = false));

            }
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

            double maxSubtreeCost = _masterNodes.Max(n => n.SubtreeCost);
            if (maxSubtreeCost <= 0) maxSubtreeCost = 1.0;

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in _masterNodes)
            {
                if (node.RawElement != null) nodeMap[node.RawElement] = node;
                node.IsCollapsed = false; // 先全部展开
            }

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();

            // 自底向上计算每个节点是否包含任何Warning/Critical子节点
            var hasWarningSubtree = new HashSet<XElement>();
            foreach (var op in relOps)
            {
                if (nodeMap.TryGetValue(op, out var vm) && vm.NodeSeverity != "Info")
                {
                    var ancestor = op;
                    while (ancestor != null && ancestor.Name.LocalName == "RelOp")
                    {
                        hasWarningSubtree.Add(ancestor);
                        ancestor = ancestor.Parent?.AncestorsAndSelf().FirstOrDefault(a => a.Name.LocalName == "RelOp");
                    }
                }
            }

            foreach (var n in _masterNodes)
            {
                if (n.HasChildren && n.RawElement != null)
                {
                    if (!hasWarningSubtree.Contains(n.RawElement) && (n.SubtreeCost / maxSubtreeCost) < 0.05)
                    {
                        n.IsCollapsed = true;
                    }
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
                    string logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                    var logFile = System.IO.Path.Combine(logDir, "CollapseLog.txt");

                    System.IO.File.AppendAllText(logFile, $"\n[{DateTime.Now:HH:mm:ss.fff}] --- START CLICK: {(node.IsCollapsed ? "Expand [+]" : "Collapse [-]")} on [{node.NodeId}] {node.PhysicalOp} ---\n");

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("==================================================");
                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Action: {(node.IsCollapsed ? "Expand [+]" : "Collapse [-]")} on Node [{node.NodeId}] {node.PhysicalOp}");

                    var oldVisibleNodes = _masterNodes.Where(n => n.IsVisible).ToList();
                    var oldVisibleConns = _masterConnections.Where(c => c.IsVisible).ToList();

                    // 仅切换当前节点的折叠状态，保留其子孙节点原有的折叠状态（状态记忆）
                    node.IsCollapsed = !node.IsCollapsed;
                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Toggled IsCollapsed to {node.IsCollapsed}");

                    // 1. 先在完整树上计算所有节点的新绝对坐标
                    ReapplyLayout();
                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] ReapplyLayout Completed");

                    // 2. 根据最新的折叠状态更新 IsVisible，触发 Nodify 容器隐藏/显示
                    UpdateGraphVisibility();
                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] UpdateGraphVisibility Completed");

                    var newVisibleNodes = _masterNodes.Where(n => n.IsVisible).ToList();
                    var newVisibleConns = _masterConnections.Where(c => c.IsVisible).ToList();

                    var addedNodes = newVisibleNodes.Except(oldVisibleNodes).ToList();
                    var removedNodes = oldVisibleNodes.Except(newVisibleNodes).ToList();

                    var addedConns = newVisibleConns.Except(oldVisibleConns).ToList();
                    var removedConns = oldVisibleConns.Except(newVisibleConns).ToList();

                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Nodes Added (Expanded): {addedNodes.Count}");
                    foreach (var n in addedNodes) sb.AppendLine($"  + [{n.NodeId}] {n.PhysicalOp} (Collapsed State: {n.IsCollapsed})");

                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Nodes Removed (Hidden): {removedNodes.Count}");
                    foreach (var n in removedNodes) sb.AppendLine($"  - [{n.NodeId}] {n.PhysicalOp}");

                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Connections Added: {addedConns.Count}");
                    foreach (var c in addedConns) sb.AppendLine($"  + [{c.Source?.NodeId}] {c.Source?.PhysicalOp} --> [{c.Target?.NodeId}] {c.Target?.PhysicalOp}");

                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Connections Removed: {removedConns.Count}");
                    foreach (var c in removedConns) sb.AppendLine($"  - [{c.Source?.NodeId}] {c.Source?.PhysicalOp} --> [{c.Target?.NodeId}] {c.Target?.PhysicalOp}");

                    sb.AppendLine("==================================================");

                    System.IO.File.AppendAllText(logFile, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                    var logFile = System.IO.Path.Combine(logDir, "CollapseLog.txt");
                    System.IO.File.AppendAllText(logFile, $"\n[{DateTime.Now:HH:mm:ss.fff}] [EXCEPTION CAUGHT]: {ex}\n");
                }
                catch { }
            }
        }

        private void UpdateGraphVisibility()
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();
            var roots = relOps.Where(r => !relOps.Any(p => PlanDiagnosticAnalyzer.GetDirectChildRelOps(p, _currentNs).Contains(r))).ToList();

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in _masterNodes)
            {
                if (node.RawElement != null) nodeMap[node.RawElement] = node;
            }

            var visibleNodeVms = new HashSet<PlanNodeViewModel>();
            var visibleConnVms = new HashSet<ConnectionViewModel>();

            // Traverse and calculate which nodes/connections should be visible
            void Traverse(XElement el, bool isVisible)
            {
                if (nodeMap.TryGetValue(el, out var vm))
                {
                    if (isVisible) visibleNodeVms.Add(vm);
                    bool childrenVisible = isVisible && !vm.IsCollapsed;

                    var children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(el, _currentNs).ToList();

                    foreach (var child in children)
                    {
                        if (nodeMap.TryGetValue(child, out var childVm))
                        {
                            var conn = _masterConnections.FirstOrDefault(c => c.Source == childVm && c.Target == vm);
                            if (conn != null)
                            {
                                if (childrenVisible) visibleConnVms.Add(conn);
                            }
                        }
                        Traverse(child, childrenVisible);
                    }
                }
            }

            foreach (var root in roots)
            {
                Traverse(root, true);
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

            ApplyLayeredLayout(_masterNodes, nodeMap, relOps, _currentNs);

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
            if (_selectedNode == null)
            {
                foreach (var conn in Connections)
                {
                    conn.IsHighlighted = true;
                }
            }
            else
            {
                foreach (var conn in Connections)
                {
                    // 高亮与当前选中节点直接相连（作为其输入或输出）的数据流连接线
                    conn.IsHighlighted = (conn.Source == _selectedNode || conn.Target == _selectedNode);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class PlanIconManager
    {
        public static string? FindIconPath(string op)
        {
            if (string.IsNullOrEmpty(op)) return null;
            string opLower = op.ToLowerInvariant().Trim();

            string name = opLower.Replace(" ", "-").Replace("_", "-");

            // 特定算子别名规则
            if (opLower.Contains("hash match") || opLower.Contains("hash")) name = "hash-match";
            else if (opLower.Contains("merge join") || opLower.Contains("merge")) name = "merge-join";
            else if (opLower.Contains("nested loops") || opLower.Contains("loops") || opLower.Contains("loop")) name = "nested-loops";
            else if (opLower.Contains("parallelism") || opLower.Contains("exchange")) name = "parallelism";
            else if (opLower.Contains("stream aggregate") || opLower.Contains("hash aggregate") || opLower.Contains("aggregate")) name = "aggregate";
            else if (opLower.Contains("compute scalar") || opLower.Contains("compute")) name = "compute-scalar";
            else if (opLower.Contains("key lookup")) name = "key-lookup";
            else if (opLower.Contains("clustered index scan")) name = "clustered-index-scan";
            else if (opLower.Contains("clustered index seek")) name = "clustered-index-seek";
            else if (opLower.Contains("index scan") || opLower.Contains("nonclustered index scan")) name = "nonclustered-index-scan";
            else if (opLower.Contains("index seek") || opLower.Contains("nonclustered index seek")) name = "nonclustered-index-seek";
            else if (opLower.Contains("table scan")) name = "table-scan";
            else if (opLower.Contains("sort")) name = "sort";
            else if (opLower.Contains("filter")) name = "filter";
            else if (opLower.Contains("top")) name = "top";
            else if (opLower.Contains("table-valued function") || opLower.Contains("table valued function")) name = "table-valued-function";
            else if (opLower.Contains("union")) name = "union";
            else if (opLower.Contains("delete")) name = "delete";
            else if (opLower.Contains("insert")) name = "insert";
            else if (opLower.Contains("update")) name = "update";

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string iconFile = $"icon-{name}.png";

            string[] searchPaths = new[]
            {
                System.IO.Path.Combine(baseDir, "ssms_icons", iconFile),
                System.IO.Path.Combine(baseDir, "..", "..", "..", "ssms_icons", iconFile),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "ssms_icons", iconFile)
            };

            foreach (var path in searchPaths)
            {
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        public static System.Windows.Media.ImageSource? GetIcon(string op)
        {
            string? path = FindIconPath(op);
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
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

        public bool IsFullPartitionScan => Partitioned == "True" && !string.IsNullOrEmpty(PartitionCount) && (PartitionRange == $"1 - {PartitionCount}" || PartitionRange == $"1-{PartitionCount}");
        public string PartitionRangeColor => IsFullPartitionScan ? "#FF0000" : "#263238"; // Bright Red
        public string PartitionLabelColor => IsFullPartitionScan ? "#FF0000" : "#546E7A"; // Bright Red

        public string HasSeekPredicates => string.IsNullOrEmpty(SeekPredicates) ? "Collapsed" : "Visible";
        public string HasPredicate => string.IsNullOrEmpty(Predicate) ? "Collapsed" : "Visible";
        public string HasOutputList => string.IsNullOrEmpty(OutputList) ? "Collapsed" : "Visible";
        public string HasPartitionInfo => Partitioned == "True" ? "Visible" : "Collapsed";

        public string NodeSeverity { get; set; } = "Info"; // Info, Warning, Critical
        public string NodeSeverityColor => NodeSeverity switch
        {
            "Critical" => "#D32F2F", // Red
            "Warning" => "#F57C00",  // Orange
            _ => "Transparent"
        };
        public string NodeSeverityBorderThickness => NodeSeverity == "Info" ? "0" : "2";

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

        public string CollapseButtonVisibility => HasChildren ? "Visible" : "Collapsed";

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

        private static Color LerpColor(Color c1, Color c2, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            byte r = (byte)(c1.R + (c2.R - c1.R) * t);
            byte g = (byte)(c1.G + (c2.G - c1.G) * t);
            byte b = (byte)(c1.B + (c2.B - c1.B) * t);
            return Color.FromRgb(r, g, b);
        }

        public Brush DynamicBackgroundBrush
        {
            get
            {
                double t = Math.Min(100, ActivePercent) / 100.0;
                // Premium Gradient: White/Gray to Vibrant Red
                Color topColor = LerpColor(Color.FromRgb(255, 255, 255), Color.FromRgb(255, 230, 230), Math.Pow(t, 0.8));
                Color botColor = LerpColor(Color.FromRgb(245, 247, 250), Color.FromRgb(255, 190, 190), Math.Pow(t, 0.6));
                return new LinearGradientBrush(topColor, botColor, 90.0);
            }
        }

        public Brush DynamicBorderBrush
        {
            get
            {
                double t = Math.Min(100, ActivePercent) / 100.0;
                // Elegant Border: Cool Blue-Gray to Deep Crimson
                Color c = LerpColor(Color.FromRgb(176, 190, 197), Color.FromRgb(211, 47, 47), Math.Pow(t, 0.7));
                return new SolidColorBrush(c);
            }
        }

        public Thickness DynamicBorderThickness => ActivePercent >= 30 ? new Thickness(2.0) : new Thickness(1.0);

        public Brush AccentBrush => OperatorType switch
        {
            "Scan" => new SolidColorBrush(Color.FromRgb(0x29, 0x62, 0xFF)),      // Deep Vibrant Blue
            "Seek" => new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53)),      // Emerald Green
            "Join" => new SolidColorBrush(Color.FromRgb(0xFF, 0x6D, 0x00)),      // Brilliant Orange
            "Parallelism" => new SolidColorBrush(Color.FromRgb(0xD5, 0x00, 0xF9)), // Neon Purple
            "Sort" => new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44)),      // Crimson Red
            "Spool" => new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0xD4)),     // Cyan/Teal
            "Compute" => new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)),   // Amber/Gold
            _ => new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B))           // Blue Gray
        };

        public string OperatorGeometry
        {
            get
            {
                return OperatorType switch
                {
                    "Scan" => "M4 4h16v16H4V4zm2 4v10h12V8H6zM4 2h16c1.1 0 2 .9 2 2v16c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2z",
                    "Seek" => "M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z",
                    "Join" => "M15 16c0-3.31-2.69-6-6-6S3 12.69 3 16s2.69 6 6 6 6-2.69 6-6zm-6 4c-2.21 0-4-1.79-4-4s1.79-4 4-4 4 1.79 4 4-1.79 4-4 4zm10-14c-3.31 0-6 2.69-6 6 0 .42.06.82.14 1.21.63-.58 1.39-.99 2.22-1.15.52-2.15 2.45-3.77 4.74-3.77 2.65 0 4.8 2.15 4.8 4.8 0 2.29-1.62 4.22-3.77 4.74-.16.83-.57 1.59-1.15 2.22.39.08.79.14 1.21.14 3.31 0 6-2.69 6-6s-2.69-6-6-6z",
                    "Sort" => "M3 18h6v-2H3v2zM3 6v2h18V6H3zm0 7h12v-2H3v2z",
                    "Parallelism" => "M14 4l2.29 2.29-2.88 2.88 1.42 1.42 2.88-2.88L20 10V4h-6zm-4 0H4v6l2.29-2.29 4.71 4.7V20h2v-8.41l-5.29-5.3L10 4z",
                    "Spool" => "M12 2C6.48 2 2 3.79 2 6v12c0 2.21 4.48 4 10 4s10-1.79 10-4V6c0-2.21-4.48-4-10-4zm0 18c-4.42 0-8-1.42-8-3.17V15c1.86 1.05 4.75 1.67 8 1.67s6.14-.62 8-1.67v1.83c0 1.75-3.58 3.17-8 3.17zm0-5c-4.42 0-8-1.42-8-3.17V10c1.86 1.05 4.75 1.67 8 1.67s6.14-.62 8-1.67v1.83c0 1.75-3.58 3.17-8 3.17zm0-5c-4.42 0-8-1.42-8-3.17S7.58 3.67 12 3.67s8 1.42 8 3.17-3.58 3.16-8 3.16z",
                    "Compute" => "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-6 2h2v2h-2V5zm0 4h2v2h-2V9zm-4-4h2v2H9V5zm0 4h2v2H9V9zm-4-4h2v2H5V5zm0 4h2v2H5V9zm14 10H5v-6h14v6zm0-8h-2V5h2v6z",
                    _ => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z"
                };
            }
        }

        public Brush CostBadgeBrush => ActivePercent >= 40 ? new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50))
                                    : ActivePercent >= 15 ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00))
                                    : new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC));

        public Brush CostBadgeForeground => ActivePercent >= 15 ? Brushes.White : Brushes.Black;

        public Brush ActualRowsBrush
        {
            get
            {
                if (ActualRowsNum <= 0 || EstRowsNum <= 0) return Brushes.DimGray;
                double ratio = ActualRowsNum / EstRowsNum;
                if (ratio > 3.0 || ratio < 0.33) return Brushes.DarkRed;      // 严重倾斜
                if (ratio > 1.5 || ratio < 0.7) return Brushes.DarkOrange;
                return new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            }
        }

        public string SkewWarning
        {
            get
            {
                if (ActualRowsNum <= 0 || EstRowsNum <= 0) return "";
                double ratio = ActualRowsNum / EstRowsNum;
                if (ratio > 5) return "↑↑ 严重高估";
                if (ratio > 2.5) return "↑ 高估";
                if (ratio < 0.2) return "↓↓ 严重低估";
                if (ratio < 0.5) return "↓ 低估";
                return "";
            }
        }

        public string HasObjectDetails => string.IsNullOrEmpty(ObjectDetails) ? "Collapsed" : "Visible";
        public string IsParallelVisible => IsParallel ? "Visible" : "Collapsed";
        public string HasWarningVisible => string.IsNullOrEmpty(Warnings) ? "Collapsed" : "Visible";
        public string HasExtraInfo => (IsParallel || !string.IsNullOrEmpty(Warnings)) ? "Visible" : "Collapsed";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ConnectionViewModel : INotifyPropertyChanged
    {
        private PlanNodeViewModel? _source;
        private PlanNodeViewModel? _target;

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

        public double RowsCount => Source != null ? (Source.ActualRowsNum > 0 ? Source.ActualRowsNum : Source.EstRowsNum) : 0;
        public double DataSizeVal => Source != null ? (Source.ActualRowsNum > 0 ? Source.ActualRowsNum * Source.AvgRowSizeNum : Source.EstRowsNum * Source.AvgRowSizeNum) : 0;

        public double ThicknessValue
        {
            get
            {
                double val = CurrentLinkMetric switch
                {
                    LinkMetricMode.RowCount => RowsCount,
                    LinkMetricMode.DataSize => DataSizeVal,
                    _ => RowsCount
                };

                if (val <= 0) return 1.5;

                // 对数-双曲正切混合缩放模型
                // W_link = W_min + (W_max - W_min) * tanh(alpha * log10(val + 1))
                double wMin = 1.5;
                double wMax = 12.0;
                double alpha = 0.25;
                double logVal = Math.Log10(val + 1);
                double thickness = wMin + (wMax - wMin) * Math.Tanh(alpha * logVal);
                return thickness;
            }
        }

        public Point SourceLocation
        {
            get
            {
                if (Source == null) return default;

                if (LayoutMode == PlanLayoutMode.Horizontal)
                {
                    if (Target == null)
                        return new Point(Source.Location.X, Source.Location.Y + 35);

                    if (Source.Location.X > Target.Location.X)
                        return new Point(Source.Location.X, Source.Location.Y + 35); // Left edge
                    else
                        return new Point(Source.Location.X + 228, Source.Location.Y + 35); // Right edge
                }
                else // Vertical layout: child is below parent (Source Y > Target Y)
                {
                    if (Target == null)
                        return new Point(Source.Location.X + 115, Source.Location.Y);

                    if (Source.Location.Y > Target.Location.Y)
                        return new Point(Source.Location.X + 115, Source.Location.Y); // Top edge
                    else
                        return new Point(Source.Location.X + 115, Source.Location.Y + 70); // Bottom edge
                }
            }
        }

        public Point TargetLocation
        {
            get
            {
                if (Target == null) return default;

                if (LayoutMode == PlanLayoutMode.Horizontal)
                {
                    if (Source == null)
                        return new Point(Target.Location.X + 228, Target.Location.Y + 35);

                    if (Source.Location.X > Target.Location.X)
                        return new Point(Target.Location.X + 228, Target.Location.Y + 35); // Right edge
                    else
                        return new Point(Target.Location.X, Target.Location.Y + 35); // Left edge
                }
                else // Vertical layout: child is below parent (Source Y > Target Y)
                {
                    if (Source == null)
                        return new Point(Target.Location.X + 115, Target.Location.Y + 70);

                    if (Source.Location.Y > Target.Location.Y)
                        return new Point(Target.Location.X + 115, Target.Location.Y + 70); // Bottom edge
                    else
                        return new Point(Target.Location.X + 115, Target.Location.Y); // Top edge
                }
            }
        }

        public double MidpointX
        {
            get
            {
                double x = (SourceLocation.X + TargetLocation.X) / 2;
                double estimatedWidth = 8 + (LabelText?.Length ?? 1) * 5.2;
                return x - estimatedWidth / 2;
            }
        }

        public double MidpointY
        {
            get
            {
                double y = (SourceLocation.Y + TargetLocation.Y) / 2;
                return y - 8;
            }
        }

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
                if (Source == null) return DefaultBrush;

                bool hasActual = Source.ActualRowsNum > 0 || (Source.ActualRowsDisplay != "N/A" && !string.IsNullOrEmpty(Source.ActualRows));
                if (!hasActual)
                {
                    return DefaultBrush;
                }

                double est = Source.EstRowsNum;
                double act = Source.ActualRowsNum;

                if (est <= 0) est = 1.0;
                if (act <= 0) act = 1.0;

                double ratio = act / est;
                if (ratio > 5.0 || ratio < 0.2)
                {
                    return RedBrush;
                }
                else if (ratio > 2.0 || ratio < 0.5)
                {
                    return OrangeBrush;
                }
                else
                {
                    return GreenBrush;
                }
            }
        }

        private static string FormatBytes(double bytes)
        {
            if (bytes >= 1024 * 1024 * 1024) return (bytes / (1024 * 1024 * 1024)).ToString("0.0") + " GB";
            if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).ToString("0.0") + " MB";
            if (bytes >= 1024) return (bytes / 1024).ToString("0.0") + " KB";
            return bytes.ToString("0") + " B";
        }

        public string LabelText
        {
            get
            {
                return CurrentLinkMetric switch
                {
                    LinkMetricMode.RowCount => PlanGraphControl.FormatNumber(RowsCount),
                    LinkMetricMode.DataSize => FormatBytes(DataSizeVal),
                    _ => PlanGraphControl.FormatNumber(RowsCount)
                };
            }
        }

        public string ToolTipText
        {
            get
            {
                if (Source == null) return "未知数据流";

                string estRowsStr = PlanGraphControl.FormatNumber(Source.EstRowsNum);
                string actRowsStr = string.IsNullOrEmpty(Source.ActualRows) || Source.ActualRows == "N/A" ? "N/A" : PlanGraphControl.FormatNumber(Source.ActualRowsNum);

                string estSizeStr = FormatBytes(Source.EstRowsNum * Source.AvgRowSizeNum);
                string actSizeStr = string.IsNullOrEmpty(Source.ActualRows) || Source.ActualRows == "N/A" ? "N/A" : FormatBytes(Source.ActualRowsNum * Source.AvgRowSizeNum);

                var sb = new StringBuilder();
                sb.AppendLine($"数据流: {Source.PhysicalOp} ➔ {Target?.PhysicalOp}");
                sb.AppendLine($"预估行数: {estRowsStr} ({Source.EstRowsNum:N0})");
                if (actRowsStr != "N/A")
                {
                    sb.AppendLine($"实际行数: {actRowsStr} ({Source.ActualRowsNum:N0})");
                }
                sb.AppendLine($"平均行宽: {Source.AvgRowSizeNum:N0} 字节");
                sb.AppendLine($"预估大小: {estSizeStr}");
                if (actSizeStr != "N/A")
                {
                    sb.AppendLine($"实际大小: {actSizeStr}");
                    double ratio = Source.EstRowsNum > 0 ? (Source.ActualRowsNum / Source.EstRowsNum) : 1.0;
                    sb.AppendLine($"估算偏差: {ratio:F2} 倍");
                    if (ratio > 5.0)
                        sb.AppendLine("⚠️ 严重低估 (可能会引发非最优物理算法选择！)");
                    else if (ratio < 0.2)
                        sb.AppendLine("⚠️ 严重高估 (可能会导致过度的内存申请排队！)");
                }
                return sb.ToString().TrimEnd();
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
            double rows = 0;
            if (value is double d) rows = d;
            else if (value is float f) rows = f;
            else if (value is int i) rows = i;
            else if (value is long l) rows = l;
            else if (value is string s && SqlXmlAnalyzer.Core.NumericParser.TryParseInvariantDouble(s, out double parsed)) rows = parsed;

            if (rows <= 0) return 1.0;

            double logVal = Math.Log10(rows) * 1.6;
            double thickness = Math.Max(1.0, Math.Min(14.0, logVal));
            return thickness;
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
