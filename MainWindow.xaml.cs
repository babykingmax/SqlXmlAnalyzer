using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace SqlXmlAnalyzer
{
    public sealed class PlanVisualNode
    {
        public string PhysicalOp { get; set; } = "";
        public string LogicalOp { get; set; } = "";
        public double Cost { get; set; }
        public string EstRows { get; set; } = "0";
        public System.Windows.Media.Brush CostColor { get; set; } = System.Windows.Media.Brushes.Black;
        public System.Windows.Media.ImageSource? OperatorIcon { get; set; }
        public List<PlanVisualNode> Children { get; set; } = new List<PlanVisualNode>();
        public XElement? Tag { get; set; }
    }

    public partial class MainWindow : Window
    {
        public Core.ViewModels.MainViewModel ViewModel { get; }
        private readonly Core.XelReader _xelReader;

        private Dictionary<string, FrameworkElement> _nodeElements = new Dictionary<string, FrameworkElement>();
        private Dictionary<(string, string), (System.Windows.Shapes.Line line, System.Windows.Shapes.Polygon arrowHead, Border label)> _arrowCache = new Dictionary<(string, string), (System.Windows.Shapes.Line line, System.Windows.Shapes.Polygon arrowHead, Border label)>();
        private List<(string fromId, string toId, string label)> _edgesForDrawing = new List<(string, string, string)>();
        
        private DeadlockTimelineParser.ParsedDeadlock? _currentTimeline;
        private DeadlockPlaybackViewModel? _playbackViewModel;
        private Dictionary<(string, string), Border> _stepBadges = new Dictionary<(string, string), Border>();

        public MainWindow(Core.XelReader xelReader = null)
        {
            InitializeComponent();
            _xelReader = xelReader ?? new Core.XelReader();
            ViewModel = new Core.ViewModels.MainViewModel();
            ViewModel.ShowMessageBox = msg => MessageBox.Show(msg);
            this.DataContext = ViewModel;
            SetupCanvasZoomPan();

            // 监听 PlanA / PlanB 快照变化，动态重构并排对比的操作符结构树
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.PlanA))
                {
                    PlanATreeView.Items.Clear();
                    if (ViewModel.PlanA != null)
                    {
                        var tree = BuildPlanTreeView(ViewModel.PlanA.Document, _showplanNs);
                        if (tree != null) PlanATreeView.Items.Add(tree);
                    }
                }
                else if (e.PropertyName == nameof(ViewModel.PlanB))
                {
                    PlanBTreeView.Items.Clear();
                    if (ViewModel.PlanB != null)
                    {
                        var tree = BuildPlanTreeView(ViewModel.PlanB.Document, _showplanNs);
                        if (tree != null) PlanBTreeView.Items.Add(tree);
                    }
                }
            };
        }

        #region 文件打开

        private async void OpenDeadlockFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "死锁文件 (*.xml;*.xdl;*.xel)|*.xml;*.xdl;*.xel|所有文件 (*.*)|*.*",
                Title = "选择死锁报告文件"
            };

            if (dlg.ShowDialog() == true)
            {
                string ext = System.IO.Path.GetExtension(dlg.FileName).ToLower();
                if (ext == ".xel")
                {
                    await AnalyzeXelFileAsync(dlg.FileName);
                }
                else
                {
                    AnalyzeDeadlockFile(dlg.FileName);
                }
            }
        }

        private async Task AnalyzeXelFileAsync(string filePath)
        {
            try
            {
                var reports = await _xelReader.ReadDeadlocksAsync(filePath);
                if (reports.Count == 0)
                {
                    MessageBox.Show("该 XEL 文件中没有找到任何 xml_deadlock_report 事件。");
                    return;
                }

                XelDeadlockSelector.ItemsSource = reports;
                XelDeadlockSelector.Visibility = Visibility.Visible;
                XelDeadlockSelector.SelectedIndex = 0; 
                MainTabControl.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Logger.LogException("MainWindow.AnalyzeXelFileAsync", ex);
                MessageBox.Show("解析 XEL 文件时发生错误: " + ex.Message);
            }
        }

        private void XelDeadlockSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (XelDeadlockSelector.SelectedItem is Core.XelDeadlockReport report)
            {
                try
                {
                    string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"deadlock_temp_{Guid.NewGuid()}.xml");
                    System.IO.File.WriteAllText(tempPath, report.DeadlockXml);
                    AnalyzeDeadlockFile(tempPath);
                }
                catch (Exception ex)
                {
                    Logger.LogException("渲染死锁图失败 (Selector_SelectionChanged)", ex);
                    MessageBox.Show("渲染死锁图失败: " + ex.Message);
                }
            }
        }

        private void OpenPlanFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "执行计划文件 (*.sqlplan;*.xml)|*.sqlplan;*.xml|所有文件 (*.*)|*.*",
                Title = "选择执行计划文件"
            };

            if (dlg.ShowDialog() == true)
            {
                AnalyzeExecutionPlanFile(dlg.FileName);
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string file = files[0];
                    string ext = System.IO.Path.GetExtension(file).ToLower();

                    if (ext == ".xml" || ext == ".xdl")
                    {
                        AnalyzeDeadlockFile(file);
                    }
                    else if (ext == ".xel")
                    {
                        await AnalyzeXelFileAsync(file);
                    }
                    else if (ext == ".sqlplan")
                    {
                        AnalyzeExecutionPlanFile(file);
                    }
                    else
                    {
                        MessageBox.Show("不支持的文件类型，请选择死锁(XML/XEL)或执行计划(.sqlplan)文件。");
                    }
                }
            }
        }

        #endregion

        #region 核心分析调用

        private XNamespace _showplanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        private void AnalyzeDeadlockFile(string filePath)
        {
            AnalyzeFile(filePath);
        }

        private void AnalyzeExecutionPlanFile(string filePath)
        {
            AnalyzeFile(filePath);
        }

        private static bool IsDeadlockXml(XDocument doc)
        {
            if (doc?.Root == null) return false;
            return doc.Root.Name.LocalName.Equals("deadlock", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExecutionPlanXml(XDocument doc)
        {
            if (doc?.Root == null) return false;
            var name = doc.Root.Name;
            if (!name.LocalName.Equals("ShowPlanXML", StringComparison.OrdinalIgnoreCase))
                return false;

            string ns = name.Namespace.NamespaceName;
            return ns == "http://schemas.microsoft.com/sqlserver/2004/07/showplan" ||
                   ns.Contains("showplan", StringComparison.OrdinalIgnoreCase);
        }

        private XDocument LoadXmlDocument(string filePath)
        {
            Logger.Info($"开始加载 XML 文件: {filePath}");
            try
            {
                var doc = XDocument.Load(filePath);
                Logger.Info("使用 XDocument.Load 成功加载文件");
                return doc;
            }
            catch (System.Xml.XmlException ex) when (ex.Message.Contains("encoding", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("BOM", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("字符", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"加载 XML 时遇到编码/BOM问题: {ex.Message}，尝试使用自动编码检测机制重试...");
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var sr = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);
                    var doc = XDocument.Load(sr);
                    Logger.Info("使用 StreamReader + 自动编码检测重试加载成功");
                    return doc;
                }
                catch (Exception innerEx)
                {
                    Logger.Error("使用自动编码检测重试加载 XML 依然失败", innerEx);
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"加载 XML 发生异常: {ex.Message}", ex);
                throw;
            }
        }

        private async void AnalyzeFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Error($"尝试分析不存在的文件: {filePath}");
                MessageBox.Show("指定的文件不存在或路径无效！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                StatusTextBlock.Text = $"正在加载并识别文件：{System.IO.Path.GetFileName(filePath)}...";
                XDocument doc = await System.Threading.Tasks.Task.Run(() => LoadXmlDocument(filePath));

                if (IsDeadlockXml(doc))
                {
                    Logger.Info($"文件被识别为死锁报告: {filePath}");
                    ViewModel.CurrentDeadlockFilePath = filePath;
                    AnalyzeDeadlockDocument(doc, filePath);
                }
                else if (IsExecutionPlanXml(doc))
                {
                    Logger.Info($"文件被识别为 SQL Server 执行计划: {filePath}");
                    ViewModel.CurrentPlanFilePath = filePath;
                    AnalyzeExecutionPlanDocument(doc, filePath);
                }
                else
                {
                    Logger.Warning($"文件格式无法自动识别: {filePath}. 根节点 LocalName: {doc.Root?.Name.LocalName}, Namespace: {doc.Root?.Name.Namespace.NamespaceName}");
                    MessageBox.Show("无法自动识别该 XML 文件的类型！\n\n请确认该文件是标准的 SQL Server 死锁 XML（根节点为 <deadlock>）或执行计划 XML（根节点为 <ShowPlanXML>）。", 
                                    "格式未识别", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusTextBlock.Text = "未知文件类型";
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("AnalyzeFile", ex);
                MessageBox.Show($"解析文件失败: {ex.Message}\n\n详细错误已记录到日志。", "分析错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "解析失败";
            }
        }

        private async void AnalyzeDeadlockDocument(XDocument doc, string filePath)
        {
            try
            {
                StatusTextBlock.Text = $"正在分析死锁文件：{System.IO.Path.GetFileName(filePath)}...";
                ViewModel.CurrentDeadlockDoc = doc;

                var result = await System.Threading.Tasks.Task.Run(() => 
                {
                    var (processes, resources, victimId) = DeadlockXmlParser.ParseDeadlockXml(doc);
                    var graph = DeadlockGraphBuilder.Build(processes, resources, victimId);
                    var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph, doc);
                    string deadlockMermaid = DeadlockGraphBuilder.GenerateMermaid(graph, true);
                    
                    var parser = new DeadlockTimelineParser();
                    var timeline = parser.Parse(doc.ToString());
                    
                    return (processes, resources, graph, patterns, deadlockMermaid, timeline);
                });

                DeadlockProcessesList.ItemsSource = result.processes;
                DeadlockResourcesList.ItemsSource = result.resources;
                DeadlockPatternsListBox.ItemsSource = result.patterns;

                _currentTimeline = result.timeline;
                _playbackViewModel = new DeadlockPlaybackViewModel(_currentTimeline.Events);
                _playbackViewModel.StepChanged += (s, e) => UpdatePlaybackGraphVisibility();
                PlaybackControl.DataContext = _playbackViewModel;
                
                foreach(var b in _stepBadges.Values) { DeadlockGraphCanvas.Children.Remove(b); }
                _stepBadges.Clear();

                BuildDeadlockWaitForTree(result.graph);
                
                UpdatePlaybackGraphVisibility();

                MainTabControl.SelectedIndex = 0;
                StatusTextBlock.Text = "死锁分析完成";
            }
            catch (Exception ex)
            {
                Logger.LogException("AnalyzeDeadlockDocument", ex);
                MessageBox.Show($"分析死锁内容失败: {ex.Message}\n\n完整错误已记录到日志文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "分析失败";
            }
        }

        private async void AnalyzeExecutionPlanDocument(XDocument doc, string filePath)
        {
            try
            {
                StatusTextBlock.Text = $"正在分析执行计划：{System.IO.Path.GetFileName(filePath)}...";
                ViewModel.CurrentPlanDoc = doc;

                var result = await System.Threading.Tasks.Task.Run(() => 
                {
                    string planMermaid = ExecutionPlanVisualizer.GenerateMermaidPlan(doc, _showplanNs);
                    string queryText = doc.Descendants(_showplanNs + "StmtSimple")
                        .FirstOrDefault()?.Attribute("StatementText")?.Value ?? "未能提取语句";
                    string docString = doc.ToString();
                    string warningsText = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, _showplanNs);
                    var mis = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, _showplanNs);
                    return (planMermaid, queryText, docString, warningsText, mis);
                });

                Logger.Info($"[ExecutionPlan] 已生成 Mermaid 代码，长度: {result.planMermaid.Length} 字符");
                BuildPlanVisualTree(doc, _showplanNs);

                ViewModel.MissingIndexes.Clear();
                foreach (var mi in result.mis)
                {
                    ViewModel.MissingIndexes.Add(mi);
                }

                PlanXmlTextBox.Text = result.docString;
                PlanStatementTextBox.Text = result.queryText.Length > 800 ? result.queryText.Substring(0, 800) + "..." : result.queryText;

                var tree = BuildPlanTreeView(doc, _showplanNs);
                PlanOperatorTree.Items.Clear();
                if (tree != null) PlanOperatorTree.Items.Add(tree);

                PlanWarningsTextBox.Text = result.warningsText;

                try
                {
                    PlanNodifyGraph?.LoadFromExecutionPlan(doc, _showplanNs);
                }
                catch (Exception ex)
                {
                    Logger.LogException("Load Nodify Graph", ex);
                }

                MainTabControl.SelectedIndex = 1;

                if (PlanGraphTabControl != null)
                {
                    PlanGraphTabControl.SelectedIndex = 1;
                }

                StatusTextBlock.Text = "执行计划分析完成";
            }
            catch (Exception ex)
            {
                Logger.LogException("AnalyzeExecutionPlanDocument", ex);
                MessageBox.Show($"分析执行计划失败: {ex.Message}\n\n完整错误已记录到日志文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "分析失败";
            }
        }

        // Helper method removed. Calling DeadlockXmlParser.ParseDeadlockXml instead.

        private void UpdatePlaybackGraphVisibility()
        {
            if (_currentTimeline == null || _playbackViewModel == null || PlaybackModeToggle.IsChecked != true)
                return;

            int currentStep = _playbackViewModel.CurrentStep;
            bool focusCritical = _playbackViewModel.FocusCriticalPath;

            var visibleNodes = new HashSet<string>();
            var visibleEdges = new HashSet<(string, string)>();

            foreach (var ev in _currentTimeline.Events)
            {
                if (ev.StepNumber > currentStep) continue;
                if (focusCritical && !ev.IsInCycle) continue;
                
                string mappedProcId = $"proc_id_{ev.ProcessId}";
                string mappedResId = ev.ResourceId.StartsWith("res_") ? ev.ResourceId.Replace("res_", "res_single_") : ev.ResourceId;
                
                visibleNodes.Add(mappedProcId);
                visibleNodes.Add(mappedResId);

                if (ev.Type == "Request")
                {
                    visibleEdges.Add((mappedProcId, mappedResId));
                }
                else if (ev.Type == "Grant")
                {
                    visibleEdges.Add((mappedResId, mappedProcId));
                }
            }

            foreach (var kvp in _nodeElements)
            {
                string id = kvp.Key;
                var el = kvp.Value;
                
                string rawId = id;
                bool isProc = id.StartsWith("proc_id_");
                if (isProc) rawId = id.Substring(8);
                else if (id.StartsWith("res_single_")) rawId = id.Replace("res_single_", "res_");
                
                bool inCycle = isProc 
                    ? (_currentTimeline.Processes.ContainsKey(rawId) && _currentTimeline.Processes[rawId].IsInCycle) 
                    : (_currentTimeline.Resources.ContainsKey(rawId) && _currentTimeline.Resources[rawId].IsInCycle);

                if (focusCritical && !inCycle)
                {
                    el.Visibility = Visibility.Collapsed;
                }
                else if (visibleNodes.Contains(id))
                {
                    el.Visibility = Visibility.Visible;
                    el.Opacity = 1.0;
                }
                else
                {
                    el.Visibility = Visibility.Visible;
                    el.Opacity = 0.2;
                }
                
                if (isProc && _currentTimeline.Processes.ContainsKey(rawId) && _currentTimeline.Processes[rawId].IsVictim)
                {
                    if (el is Border b && b.Child is Grid)
                    {
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                        b.BorderThickness = new Thickness(3);
                        if (currentStep >= _currentTimeline.Events.FirstOrDefault(x => x.Type == "Victim")?.StepNumber)
                            b.Background = new SolidColorBrush(Color.FromArgb(50, 211, 47, 47));
                        else
                            b.Background = Brushes.White;
                    }
                }
            }

            foreach (var edge in _arrowCache)
            {
                var idPair = edge.Key;
                var visuals = edge.Value;
                var relatedEvent = _currentTimeline.Events.FirstOrDefault(e => 
                    (e.Type == "Request" && e.ProcessId == idPair.Item1 && e.ResourceId == idPair.Item2) ||
                    (e.Type == "Grant" && e.ResourceId == idPair.Item1 && e.ProcessId == idPair.Item2));

                bool inCycle = relatedEvent != null && relatedEvent.IsInCycle;

                if (focusCritical && !inCycle)
                {
                    visuals.line.Visibility = Visibility.Collapsed;
                    visuals.arrowHead.Visibility = Visibility.Collapsed;
                    visuals.label.Visibility = Visibility.Collapsed;
                    if (_stepBadges.TryGetValue(idPair, out var badge)) badge.Visibility = Visibility.Collapsed;
                }
                else if (visibleEdges.Contains(idPair))
                {
                    visuals.line.Visibility = Visibility.Visible;
                    visuals.line.Opacity = 1.0;
                    visuals.line.StrokeDashArray = null; 
                    visuals.arrowHead.Visibility = Visibility.Visible;
                    visuals.arrowHead.Opacity = 1.0;
                    visuals.label.Visibility = Visibility.Visible;
                    visuals.label.Opacity = 1.0;
                    
                    if (relatedEvent != null)
                    {
                        if (!_stepBadges.TryGetValue(idPair, out var badge))
                        {
                            badge = new Border
                            {
                                Background = new SolidColorBrush(Color.FromRgb(30, 136, 229)),
                                CornerRadius = new CornerRadius(8),
                                Width = 16, Height = 16,
                                Child = new TextBlock { Text = relatedEvent.StepNumber.ToString(), Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                            };
                            _stepBadges[idPair] = badge;
                            DeadlockGraphCanvas.Children.Add(badge);
                            double x1 = visuals.line.X1, y1 = visuals.line.Y1, x2 = visuals.line.X2, y2 = visuals.line.Y2;
                            Canvas.SetLeft(badge, (x1 + x2) / 2 + 10);
                            Canvas.SetTop(badge, (y1 + y2) / 2 - 15);
                        }
                        badge.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    visuals.line.Visibility = Visibility.Visible;
                    visuals.line.Opacity = 0.2;
                    visuals.line.StrokeDashArray = new DoubleCollection { 2, 2 };
                    visuals.arrowHead.Visibility = Visibility.Visible;
                    visuals.arrowHead.Opacity = 0.2;
                    visuals.label.Visibility = Visibility.Visible;
                    visuals.label.Opacity = 0.2;
                    if (_stepBadges.TryGetValue(idPair, out var badge)) badge.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void PlaybackModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            PlaybackControl.Visibility = Visibility.Visible;
            if (_playbackViewModel != null)
            {
                _playbackViewModel.CurrentStep = 0;
            }
            UpdatePlaybackGraphVisibility();
        }

        private void PlaybackModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            PlaybackControl.Visibility = Visibility.Collapsed;
            if (_playbackViewModel != null)
            {
                _playbackViewModel.IsPlaying = false;
            }
            
            foreach (var el in _nodeElements.Values)
            {
                el.Visibility = Visibility.Visible;
                el.Opacity = 1.0;
                if (el is Border b)
                {
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(176, 190, 197));
                    b.BorderThickness = new Thickness(1.5);
                    b.Background = Brushes.White;
                }
            }
            foreach (var edge in _arrowCache.Values)
            {
                edge.line.Visibility = Visibility.Visible;
                edge.line.Opacity = 1.0;
                edge.arrowHead.Visibility = Visibility.Visible;
                edge.arrowHead.Opacity = 1.0;
                edge.label.Visibility = Visibility.Visible;
                edge.label.Opacity = 1.0;
                if (edge.line.Stroke is SolidColorBrush sb && sb.Color == Color.FromRgb(56, 142, 60))
                    edge.line.StrokeDashArray = new DoubleCollection { 4, 3 };
                else
                    edge.line.StrokeDashArray = null;
            }
            foreach (var b in _stepBadges.Values)
            {
                b.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildDeadlockWaitForTree(DeadlockGraph graph)
        {
            DrawDeadlockBipartiteGraph(graph);
            Dispatcher.BeginInvoke(new Action(() => DoZoomToFitDeadlock()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private readonly Dictionary<string, Point> _nodePositions = new();
        private readonly Dictionary<string, (string LockType, string ObjectName)> _resourceGroupDetails = new();

        private sealed class CollapsedProcess
        {
            public string Spid { get; set; } = "";
            public string PrimaryId { get; set; } = "";
            public int ThreadCount { get; set; }
            public List<DeadlockProcess> Threads { get; set; } = new();
            public DeadlockProcess PrimaryProcess => Threads.FirstOrDefault(t => t.Ecid == "0") ?? Threads.First();
        }

        private sealed class CollapsedResource
        {
            public string Id { get; set; } = "";
            public string LockType { get; set; } = "";
            public string ObjectName { get; set; } = "";
            public string IndexName { get; set; } = "";
            public int LockCount { get; set; }
            public List<LockResource> RawResources { get; set; } = new();
            public string Dbid => RawResources.FirstOrDefault()?.Dbid ?? "";
            
            public HashSet<string> OwnerSpids { get; set; } = new();
            public HashSet<string> WaiterSpids { get; set; } = new();
            
            public string OwnerModes => string.Join(", ", RawResources.SelectMany(r => r.Owners).Select(o => o.Mode).Distinct());
            public string WaiterModes => string.Join(", ", RawResources.SelectMany(r => r.Waiters).Select(w => w.Mode).Distinct());
        }

        private void DrawDeadlockBipartiteGraph(DeadlockGraph graph)
        {
            DeadlockGraphCanvas.Children.Clear();
            _nodePositions.Clear();
            _nodeElements.Clear();
            _edgesForDrawing.Clear();
            _arrowCache.Clear();
            _resourceGroupDetails.Clear();

            var processes = graph.Processes.DistinctBy(p => p.Id).ToList();
            var resources = graph.Resources.ToList();

            if (processes.Count == 0)
            {
                var tb = new TextBlock { Text = "无有效的死锁进程数据", Margin = new Thickness(20), FontSize = 12, Foreground = Brushes.Gray };
                DeadlockGraphCanvas.Children.Add(tb);
                return;
            }

            // 1. 不再按 SPID 聚合，保留所有独立进程节点以展示N节点全貌（完全匹配原始分析图）
            var collapsedProcesses = processes.Select(p => new CollapsedProcess
            {
                Spid = p.Spid,
                PrimaryId = p.Id,
                ThreadCount = 1,
                Threads = new List<DeadlockProcess> { p }
            }).ToList();

            // 2. 不再强制聚合物理锁资源节点，保留所有独立资源
            var collapsedResources = resources.Select((r, idx) => {
                var collapsed = new CollapsedResource
                {
                    Id = $"res_single_{idx}",
                    LockType = r.LockType,
                    ObjectName = r.ObjectName,
                    IndexName = r.IndexName,
                    LockCount = 1,
                    RawResources = new List<LockResource> { r }
                };

                // 此时直接映射具体的 Thread ID (p.Id)，而非 SPID
                foreach (var owner in r.Owners)
                {
                    collapsed.OwnerSpids.Add(owner.Id);
                }
                foreach (var waiter in r.Waiters)
                {
                    collapsed.WaiterSpids.Add(waiter.Id);
                }

                return collapsed;
            }).ToList();

            // 节点尺寸
            double procW = 220, procH = 90;
            double resW = 160, resH = 50;

            // 还原缩放和平移，使每次打开新文件时居中
            DeadlockScaleTransform.ScaleX = 1.0;
            DeadlockScaleTransform.ScaleY = 1.0;
            DeadlockTranslateTransform.X = 0;
            DeadlockTranslateTransform.Y = 0;

            // 环形布局（Circular Layout）参数计算，参考 SQL_Deadlock_Dashboard 风格
            double canvasWidth = DeadlockCanvasBorder.ActualWidth > 0 ? DeadlockCanvasBorder.ActualWidth : 800;
            double canvasHeight = DeadlockCanvasBorder.ActualHeight > 0 ? DeadlockCanvasBorder.ActualHeight : 600;
            double centerX = canvasWidth / 2;
            double centerY = canvasHeight / 2;
            
            // 动态计算半径，防止节点重叠
            int totalNodes = collapsedProcesses.Count + collapsedResources.Count;
            double minRadius = 250;
            double dynamicRadius = Math.Max(minRadius, (totalNodes * 120) / (2 * Math.PI)); 
            double radiusX = dynamicRadius;
            double radiusY = dynamicRadius * 0.8; // 稍微扁一点的椭圆更契合宽屏
            
            int nodeIndex = 0;

            // 3. 绘制并排版独立的进程节点（环形分布）
            for (int i = 0; i < collapsedProcesses.Count; i++)
            {
                var collapsedProc = collapsedProcesses[i];
                var proc = collapsedProc.PrimaryProcess;
                bool isVictim = collapsedProc.Threads.Any(t => t.Id == graph.VictimProcessId);
                string nodeId = $"proc_id_{collapsedProc.PrimaryId}";

                double angle = 2 * Math.PI * nodeIndex / totalNodes;
                double x = centerX + radiusX * Math.Cos(angle) - procW / 2;
                double y = centerY + radiusY * Math.Sin(angle) - procH / 2;

                DrawDraggableProcessNode(x, y, procW, procH, proc, isVictim, nodeId, collapsedProc.ThreadCount);
                nodeIndex++;
            }

            // 4. 绘制并排版独立的资源节点（环形分布）
            for (int j = 0; j < collapsedResources.Count; j++)
            {
                var collapsedRes = collapsedResources[j];
                var res = collapsedRes.RawResources.First();
                
                // 缓存映射明细以供联动
                _resourceGroupDetails[collapsedRes.Id] = (collapsedRes.LockType, collapsedRes.ObjectName);

                double angle = 2 * Math.PI * nodeIndex / totalNodes;
                double x = centerX + radiusX * Math.Cos(angle) - resW / 2;
                double y = centerY + radiusY * Math.Sin(angle) - resH / 2;

                DrawDraggableResourceNode(x, y, resW, resH, res, collapsedRes.Id, collapsedRes.LockCount);
                nodeIndex++;
            }

            // 5. 绘制逻辑有向箭头边（Thread 进程 -> 独立物理资源）
            foreach (var collapsedRes in collapsedResources)
            {
                var rawRes = collapsedRes.RawResources.First();

                // 等待边（Waiters）：Thread -> 资源组
                foreach (var waiterId in collapsedRes.WaiterSpids)
                {
                    string waiterNodeId = $"proc_id_{waiterId}";
                    var waiter = rawRes.Waiters.FirstOrDefault(w => w.Id == waiterId);
                    string mode = waiter?.Mode ?? "";
                    if (string.IsNullOrEmpty(mode)) mode = waiter?.RequestType ?? "";
                    if (string.IsNullOrEmpty(mode) && rawRes.LockType == "exchangeEvent") mode = "Sync";
                    DrawArrowBetweenNodes(waiterNodeId, collapsedRes.Id, string.IsNullOrEmpty(mode) ? "Req" : $"Req: {mode}", isWaitEdge: true);
                }

                // 持有边（Owners）：资源组 -> Thread
                foreach (var ownerId in collapsedRes.OwnerSpids)
                {
                    string ownerNodeId = $"proc_id_{ownerId}";
                    var owner = rawRes.Owners.FirstOrDefault(o => o.Id == ownerId);
                    string mode = owner?.Mode ?? "";
                    if (string.IsNullOrEmpty(mode) && rawRes.LockType == "exchangeEvent") mode = "Sync";
                    DrawArrowBetweenNodes(collapsedRes.Id, ownerNodeId, string.IsNullOrEmpty(mode) ? "Own" : $"Own: {mode}", isWaitEdge: false);
                }
            }

            // 添加底部提示标签
            var tip = new TextBlock
            {
                Text = "⚡ [全景节点引擎已激活] 展示死锁快照中的所有独立并行线程与独立物理页锁（N节点环形拓扑），与底层 XML 的连接路径完全对应一致。",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.SlateGray,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetLeft(tip, 50);
            Canvas.SetTop(tip, centerY + radiusY + 60);
            DeadlockGraphCanvas.Children.Add(tip);
        }

        private FrameworkElement DrawDraggableProcessNode(double x, double y, double w, double h, DeadlockProcess proc, bool isVictim, string id, int threadCount)
        {
            var card = new Border
            {
                Width = w,
                Height = h,
                Background = isVictim ? new SolidColorBrush(Color.FromRgb(255, 240, 240)) : new SolidColorBrush(Color.FromRgb(240, 248, 255)),
                BorderBrush = isVictim ? new SolidColorBrush(Color.FromRgb(220, 50, 50)) : new SolidColorBrush(Color.FromRgb(70, 130, 180)),
                BorderThickness = isVictim ? new Thickness(2.5) : new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Effect = new System.Windows.Media.Effects.DropShadowEffect 
                { 
                    Color = Colors.Gray, 
                    Direction = 315, 
                    ShadowDepth = 2, 
                    Opacity = 0.3,
                    BlurRadius = 4
                },
                Tag = id
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 顶部标题栏
            var headerBar = new Border
            {
                Background = isVictim ? new SolidColorBrush(Color.FromRgb(220, 50, 50)) : new SolidColorBrush(Color.FromRgb(70, 130, 180)),
                CornerRadius = new CornerRadius(4, 4, 0, 0)
            };
            var headerText = new TextBlock
            {
                Text = $"{(isVictim ? "💀 " : "👤 ")}SPID {proc.Spid} [{(isVictim ? "Victim" : "Survivor")}]" + (threadCount > 1 ? $" ({threadCount} 线程)" : ""),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            };
            headerBar.Child = headerText;
            Grid.SetRow(headerBar, 0);
            mainGrid.Children.Add(headerBar);

            // 内容展示区
            var contentStack = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };

            // 数据库与事务名称
            string dbTxText = $"DB: {(!string.IsNullOrEmpty(proc.CurrentDbName) ? proc.CurrentDbName : "Unknown")}";
            if (!string.IsNullOrEmpty(proc.TransactionName))
                dbTxText += $" | Tx: {proc.TransactionName}";

            contentStack.Children.Add(new TextBlock
            {
                Text = dbTxText,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            // 登录名和主机名
            string loginHost = $"User: {proc.Loginname} ({proc.Hostname})";
            contentStack.Children.Add(new TextBlock
            {
                Text = loginHost,
                FontSize = 9,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            // SQL 语句
            string sql = "";
            if (proc.ExecutionStack.Count > 0 && !string.IsNullOrEmpty(proc.ExecutionStack[0].Statement))
            {
                sql = proc.ExecutionStack[0].Statement;
            }
            else if (!string.IsNullOrEmpty(proc.Inputbuf))
            {
                sql = proc.Inputbuf;
            }
            else
            {
                sql = "No statement info";
            }

            sql = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ").Trim();
            if (sql.Length > 85)
                sql = sql.Substring(0, 82) + "...";

            var sqlText = new TextBlock
            {
                Text = sql,
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 120)),
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = new ToolTip 
                { 
                    Content = new TextBlock 
                    { 
                        Text = string.IsNullOrEmpty(proc.Inputbuf) ? sql : proc.Inputbuf, 
                        MaxWidth = 400, 
                        TextWrapping = TextWrapping.Wrap 
                    } 
                }
            };
            contentStack.Children.Add(sqlText);

            Grid.SetRow(contentStack, 1);
            mainGrid.Children.Add(contentStack);
            card.Child = mainGrid;

            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            DeadlockGraphCanvas.Children.Add(card);

            _nodeElements[id] = card;
            _nodePositions[id] = new Point(x, y);

            AttachDragBehavior(card, id);

            return card;
        }

        private FrameworkElement DrawDraggableResourceNode(double x, double y, double w, double h, LockResource res, string id, int lockCount)
        {
            var container = new Grid
            {
                Width = w,
                Height = h,
                Tag = id,
                Background = Brushes.Transparent
            };

            var points = new PointCollection
            {
                new Point(0, h / 2),
                new Point(12, 0),
                new Point(w - 12, 0),
                new Point(w, h / 2),
                new Point(w - 12, h),
                new Point(12, h)
            };

            var poly = new System.Windows.Shapes.Polygon
            {
                Points = points,
                Fill = new SolidColorBrush(Color.FromRgb(255, 248, 225)), // #FFF8E1
                Stroke = new SolidColorBrush(Color.FromRgb(255, 179, 0)), // #FFB300
                StrokeThickness = 2,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Gray,
                    Direction = 315,
                    ShadowDepth = 1.5,
                    Opacity = 0.25,
                    BlurRadius = 3
                }
            };
            container.Children.Add(poly);

            var textStack = new StackPanel 
            { 
                VerticalAlignment = VerticalAlignment.Center, 
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(14, 0, 14, 0),
                IsHitTestVisible = false
            };

            string lockTypeText = $"{res.LockType.ToUpperInvariant()}";
            if (lockCount > 1)
                lockTypeText += $" ({lockCount} 锁)";
            else if (!string.IsNullOrEmpty(res.Dbid))
                lockTypeText += $" (DB: {res.Dbid})";

            textStack.Children.Add(new TextBlock
            {
                Text = lockTypeText,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(183, 28, 28)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            string objName = !string.IsNullOrEmpty(res.ObjectName) ? res.ObjectName : "(Object)";
            if (objName.Contains("."))
            {
                var parts = objName.Split('.');
                if (parts.Length > 1)
                    objName = string.Join(".", parts.Skip(Math.Max(0, parts.Length - 2)));
            }

            if (!string.IsNullOrEmpty(res.IndexName))
                objName += $" ({res.IndexName})";

            var objTextBlock = new TextBlock
            {
                Text = objName,
                FontSize = 8.5,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = w - 28,
                ToolTip = new ToolTip { Content = res.ObjectName + (!string.IsNullOrEmpty(res.IndexName) ? $" ({res.IndexName})" : "") }
            };
            textStack.Children.Add(objTextBlock);

            container.Children.Add(textStack);

            Canvas.SetLeft(container, x);
            Canvas.SetTop(container, y);
            DeadlockGraphCanvas.Children.Add(container);

            _nodeElements[id] = container;
            _nodePositions[id] = new Point(x, y);

            AttachDragBehavior(container, id);

            return container;
        }

        private void AttachDragBehavior(FrameworkElement element, string id)
        {
            bool isDragging = false;
            Point lastPos = default;

            element.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    // 双击联动同步选择侧边栏
                    if (id.StartsWith("res_single_"))
                    {
                        if (_resourceGroupDetails.TryGetValue(id, out var details))
                        {
                            var resItem = DeadlockResourcesList.ItemsSource?.Cast<LockResource>()
                                .FirstOrDefault(r => r.ObjectName == details.ObjectName && r.LockType == details.LockType);
                            if (resItem != null)
                            {
                                DeadlockResourcesList.SelectedItem = resItem;
                                DeadlockResourcesList.ScrollIntoView(resItem);
                            }
                        }
                    }
                    else if (id.StartsWith("proc_id_"))
                    {
                        string procId = id.Replace("proc_id_", "");
                        var procItem = DeadlockProcessesList.ItemsSource?.Cast<DeadlockProcess>().FirstOrDefault(p => p.Id == procId);
                        if (procItem != null)
                        {
                            DeadlockProcessesList.SelectedItem = procItem;
                            DeadlockProcessesList.ScrollIntoView(procItem);
                        }
                    }
                    e.Handled = true;
                    return;
                }

                isDragging = true;
                lastPos = e.GetPosition(DeadlockGraphCanvas);
                element.CaptureMouse();
                e.Handled = true;
            };

            element.MouseMove += (s, e) =>
            {
                if (isDragging)
                {
                    var currentPos = e.GetPosition(DeadlockGraphCanvas);
                    double dx = currentPos.X - lastPos.X;
                    double dy = currentPos.Y - lastPos.Y;

                    double newLeft = Canvas.GetLeft(element) + dx;
                    double newTop = Canvas.GetTop(element) + dy;

                    Canvas.SetLeft(element, newLeft);
                    Canvas.SetTop(element, newTop);

                    _nodePositions[id] = new Point(newLeft, newTop);

                    lastPos = currentPos;

                    UpdateConnectionsForNode(id);
                }
            };

            element.MouseLeftButtonUp += (s, e) =>
            {
                if (isDragging)
                {
                    isDragging = false;
                    element.ReleaseMouseCapture();
                    e.Handled = true;
                }
            };
        }

        private Point _lastPanPoint;
        private bool _isPanning = false;

        private void SetupCanvasZoomPan()
        {
            DeadlockGraphCanvas.MouseWheel += (s, e) =>
            {
                double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
                double newScale = DeadlockScaleTransform.ScaleX * zoomFactor;

                if (newScale < 0.1 || newScale > 10) return;

                Point mousePos = e.GetPosition(DeadlockGraphCanvas);

                double absX = (mousePos.X - DeadlockTranslateTransform.X) / DeadlockScaleTransform.ScaleX;
                double absY = (mousePos.Y - DeadlockTranslateTransform.Y) / DeadlockScaleTransform.ScaleY;

                DeadlockScaleTransform.ScaleX = newScale;
                DeadlockScaleTransform.ScaleY = newScale;

                DeadlockTranslateTransform.X = mousePos.X - absX * newScale;
                DeadlockTranslateTransform.Y = mousePos.Y - absY * newScale;
                e.Handled = true;
            };

            DeadlockGraphCanvas.MouseDown += (s, e) =>
            {
                if (e.MiddleButton == MouseButtonState.Pressed || e.LeftButton == MouseButtonState.Pressed)
                {
                    _isPanning = true;
                    _lastPanPoint = e.GetPosition(DeadlockCanvasBorder);
                    DeadlockGraphCanvas.CaptureMouse();
                    e.Handled = true;
                }
            };

            DeadlockGraphCanvas.MouseMove += (s, e) =>
            {
                if (_isPanning)
                {
                    Point current = e.GetPosition(DeadlockCanvasBorder);
                    double dx = current.X - _lastPanPoint.X;
                    double dy = current.Y - _lastPanPoint.Y;

                    DeadlockTranslateTransform.X += dx;
                    DeadlockTranslateTransform.Y += dy;

                    _lastPanPoint = current;
                    e.Handled = true;
                }
            };

            DeadlockGraphCanvas.MouseUp += (s, e) =>
            {
                if (_isPanning)
                {
                    _isPanning = false;
                    DeadlockGraphCanvas.ReleaseMouseCapture();
                    e.Handled = true;
                }
            };
        }

        private void UpdateConnectionsForNode(string movedId)
        {
            var edgesToUpdate = _edgesForDrawing.Where(e => e.fromId == movedId || e.toId == movedId).ToList();

            foreach (var edge in edgesToUpdate)
            {
                var key = (edge.fromId, edge.toId);
                if (_arrowCache.TryGetValue(key, out var cached))
                {
                    var (x1, y1, x2, y2) = CalculateConnectionPoints(edge.fromId, edge.toId);

                    cached.line.X1 = x1;
                    cached.line.Y1 = y1;
                    cached.line.X2 = x2;
                    cached.line.Y2 = y2;

                    if (cached.arrowHead != null)
                    {
                        UpdateArrowHeadPosition(cached.arrowHead, new Point(x2, y2), new Point(x1, y1));
                    }

                    if (cached.label != null)
                    {
                        double labelW = cached.label.ActualWidth > 0 ? cached.label.ActualWidth : 50;
                        double labelH = cached.label.ActualHeight > 0 ? cached.label.ActualHeight : 16;
                        Canvas.SetLeft(cached.label, (x1 + x2) / 2 - labelW / 2);
                        Canvas.SetTop(cached.label, (y1 + y2) / 2 - labelH / 2);
                    }
                }
            }
        }

        private (double x1, double y1, double x2, double y2) CalculateConnectionPoints(string fromId, string toId)
        {
            double procW = 220, procH = 90;
            double resW = 160, resH = 50;

            bool fromIsResource = fromId.StartsWith("res_");
            bool toIsResource   = toId.StartsWith("res_");

            double fromW = fromIsResource ? resW : procW;
            double fromH = fromIsResource ? resH : procH;
            double toW = toIsResource ? resW : procW;
            double toH = toIsResource ? resH : procH;

            Point fromTopLeft = _nodePositions.TryGetValue(fromId, out var fp) ? fp : new Point(80, 150);
            Point toTopLeft   = _nodePositions.TryGetValue(toId, out var tp) ? tp : new Point(400, 150);

            Point fromCenter = new Point(fromTopLeft.X + fromW / 2, fromTopLeft.Y + fromH / 2);
            Point toCenter   = new Point(toTopLeft.X + toW / 2, toTopLeft.Y + toH / 2);

            double dx = toCenter.X - fromCenter.X;
            double dy = toCenter.Y - fromCenter.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 0.1) dist = 0.1;
            double ux = dx / dist;
            double uy = dy / dist;

            double factorFrom = Math.Min((fromW / 2) / Math.Max(0.001, Math.Abs(ux)), (fromH / 2) / Math.Max(0.001, Math.Abs(uy)));
            double factorTo   = Math.Min((toW / 2) / Math.Max(0.001, Math.Abs(ux)), (toH / 2) / Math.Max(0.001, Math.Abs(uy)));

            double gap = 3;
            double x1 = fromCenter.X + ux * (factorFrom + gap);
            double y1 = fromCenter.Y + uy * (factorFrom + gap);
            double x2 = toCenter.X - ux * (factorTo + gap);
            double y2 = toCenter.Y - uy * (factorTo + gap);

            return (x1, y1, x2, y2);
        }

        private System.Windows.Shapes.Polygon CreateArrowHead(Point tip, Point fromPoint, Brush fill)
        {
            double dx = tip.X - fromPoint.X;
            double dy = tip.Y - fromPoint.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.1) length = 0.1;

            double ux = dx / length;
            double uy = dy / length;

            double arrowSize = 10;
            double arrowWidth = 6;

            var p1 = tip;
            var p2 = new Point(tip.X - ux * arrowSize - uy * arrowWidth, tip.Y - uy * arrowSize + ux * arrowWidth);
            var p3 = new Point(tip.X - ux * arrowSize + uy * arrowWidth, tip.Y - uy * arrowSize - ux * arrowWidth);

            return new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { p1, p2, p3 },
                Fill = fill,
                Stroke = fill,
                StrokeThickness = 0.5
            };
        }

        private void UpdateArrowHeadPosition(System.Windows.Shapes.Polygon arrowHead, Point tip, Point fromPoint)
        {
            if (arrowHead == null) return;

            double dx = tip.X - fromPoint.X;
            double dy = tip.Y - fromPoint.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.1) length = 0.1;

            double ux = dx / length;
            double uy = dy / length;

            double arrowSize = 10;
            double arrowWidth = 6;

            var p1 = tip;
            var p2 = new Point(tip.X - ux * arrowSize - uy * arrowWidth, tip.Y - uy * arrowSize + ux * arrowWidth);
            var p3 = new Point(tip.X - ux * arrowSize + uy * arrowWidth, tip.Y - uy * arrowSize - ux * arrowWidth);

            arrowHead.Points[0] = p1;
            arrowHead.Points[1] = p2;
            arrowHead.Points[2] = p3;
        }

        private void DrawArrowBetweenNodes(string fromId, string toId, string label, bool isWaitEdge)
        {
            var (x1, y1, x2, y2) = CalculateConnectionPoints(fromId, toId);

            var brush = isWaitEdge ? new SolidColorBrush(Color.FromRgb(211, 47, 47)) : new SolidColorBrush(Color.FromRgb(56, 142, 60));
            
            var line = new System.Windows.Shapes.Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = brush,
                StrokeThickness = isWaitEdge ? 2.5 : 2.0
            };

            if (!isWaitEdge)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 3 };
            }

            DeadlockGraphCanvas.Children.Add(line);

            var arrowHead = CreateArrowHead(new Point(x2, y2), new Point(x1, y1), brush);
            DeadlockGraphCanvas.Children.Add(arrowHead);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                BorderBrush = brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false
            };
            var tb = new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = brush
            };
            border.Child = tb;

            Canvas.SetLeft(border, (x1 + x2) / 2 - 25);
            Canvas.SetTop(border, (y1 + y2) / 2 - 8);
            DeadlockGraphCanvas.Children.Add(border);

            var key = (fromId, toId);
            _arrowCache[key] = (line, arrowHead, border);
            _edgesForDrawing.Add((fromId, toId, label));
        }

        private void BuildPlanVisualTree(XDocument doc, XNamespace ns)
        {
            PlanVisualTree.ItemsSource = null;

            var rootRelOp = doc.Descendants(ns + "RelOp").FirstOrDefault();
            if (rootRelOp != null)
            {
                var rootNode = CreatePlanVisualNode(rootRelOp, ns);
                PlanVisualTree.ItemsSource = new List<PlanVisualNode> { rootNode };
            }
        }

        private PlanVisualNode CreatePlanVisualNode(XElement relOp, XNamespace ns)
        {
            string phys = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";
            string logical = relOp.Attribute("LogicalOp")?.Value ?? "";
            string costStr = relOp.Attribute("EstimatedTotalSubtreeCost")?.Value ?? "0";
            string estRows = relOp.Attribute("EstimatedRows")?.Value ?? "0";

            double cost = 0;
            double.TryParse(costStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out cost);

            var costBrush = System.Windows.Media.Brushes.Black;
            if (cost > 10.0) costBrush = System.Windows.Media.Brushes.Red;
            else if (cost > 5.0) costBrush = System.Windows.Media.Brushes.DarkOrange;

            var node = new PlanVisualNode
            {
                PhysicalOp = phys,
                LogicalOp = logical,
                Cost = cost,
                EstRows = estRows,
                CostColor = costBrush,
                Tag = relOp,
                OperatorIcon = PlanIconManager.GetIcon(phys)
            };

            foreach (var child in PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns))
            {
                node.Children.Add(CreatePlanVisualNode(child, ns));
            }

            return node;
        }

        // 简单构建执行计划 TreeView 节点 (参考 Plan Explorer 左侧树)
        private TreeViewItem? BuildPlanTreeView(XDocument doc, XNamespace ns)
        {
            var rootRelOp = doc.Descendants(ns + "RelOp").FirstOrDefault();
            if (rootRelOp == null) return null;

            return BuildRelOpNode(rootRelOp, ns);
        }

        private TreeViewItem BuildRelOpNode(XElement relOp, XNamespace ns)
        {
            string phys = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";
            string cost = relOp.Attribute("EstimatedTotalSubtreeCost")?.Value ?? "0";

            var item = new TreeViewItem
            {
                Header = $"{phys} (Cost: {cost})",
                Tag = relOp
            };

            foreach (var child in PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns))
            {
                item.Items.Add(BuildRelOpNode(child, ns));
            }

            return item;
        }

        #endregion

        #region 其他功能

        private void ExportObfuscatedPlan_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentPlanDoc == null)
            {
                MessageBox.Show("请先加载一个执行计划文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "执行计划文件 (*.sqlplan)|*.sqlplan|XML文件 (*.xml)|*.xml",
                Title = "导出脱敏后的执行计划",
                FileName = "Obfuscated_Plan.sqlplan"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    StatusTextBlock.Text = "正在生成脱敏计划...";
                    var maskedDoc = SqlXmlAnalyzer.Core.Services.PlanObfuscatorService.ObfuscatePlan(ViewModel.CurrentPlanDoc);
                    maskedDoc.Save(dlg.FileName);
                    MessageBox.Show($"脱敏后的执行计划已保存至:\n{dlg.FileName}\n\n安全提示：敏感表名和SQL语句已完全替换，但该文件仍可被 SSMS 解析。", "脱敏成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusTextBlock.Text = "就绪";
                }
                catch (Exception ex)
                {
                    Logger.LogException("ExportObfuscatedPlan_Click", ex);
                    MessageBox.Show($"导出时发生错误:\n{ex.Message}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusTextBlock.Text = "脱敏导出失败";
                }
            }
        }

        private void GenerateHtmlReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainTabControl.SelectedIndex == 0) // Deadlock
                {
                    if (ViewModel.CurrentDeadlockDoc == null || string.IsNullOrEmpty(ViewModel.CurrentDeadlockFilePath))
                    {
                        MessageBox.Show("请先打开并分析一个死锁 XML 文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var (processes, resources, victimId) = DeadlockXmlParser.ParseDeadlockXml(ViewModel.CurrentDeadlockDoc);
                    var graph = DeadlockGraphBuilder.Build(processes, resources, victimId);
                    string mermaid = DeadlockGraphBuilder.GenerateMermaid(graph, true);

                    string summaryText = $"死锁文件: {Path.GetFileName(ViewModel.CurrentDeadlockFilePath)}\n受害者进程: {victimId}\n参与 SPID: {string.Join(", ", processes.Select(p => p.Spid).Distinct())}";
                    
                    var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph, ViewModel.CurrentDeadlockDoc);
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("<h3>🔍 死锁模式自动诊断：</h3>");
                    foreach (var p in patterns)
                    {
                        sb.AppendLine($"<div style='border: 1px solid #ffcccc; background: #fff5f5; padding: 10px; margin-bottom: 10px; border-radius: 4px;'>");
                        sb.AppendLine($"  <strong style='color: {(p.Severity == "High" ? "red" : "orange")};'>【{p.TypeName}】 ({p.Severity})</strong><br/>");
                        sb.AppendLine($"  <strong>描述:</strong> {p.Description}<br/>");
                        sb.AppendLine($"  <strong>可能原因:</strong> {p.LikelyCause}<br/>");
                        sb.AppendLine($"  <strong>推荐措施:</strong> <span style='color: green;'>{p.Recommendation.Replace("\n", "<br/>")}</span>");
                        sb.AppendLine($"</div>");
                    }

                    if (!string.IsNullOrWhiteSpace(ViewModel.DeadlockPatternText))
                    {
                        sb.AppendLine("<h3>📋 分析与选中项详情：</h3>");
                        sb.AppendLine($"<pre style='background:#f4f4f4; padding:10px; border-radius:4px; font-family:Consolas, monospace; white-space: pre-wrap;'>{System.Net.WebUtility.HtmlEncode(ViewModel.DeadlockPatternText)}</pre>");
                    }

                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "HTML 报告 (*.html)|*.html",
                        FileName = $"DeadlockReport_{Path.GetFileNameWithoutExtension(ViewModel.CurrentDeadlockFilePath)}.html",
                        Title = "保存死锁分析报告"
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        HtmlReportGenerator.SaveReport(ViewModel.CurrentDeadlockFilePath, "Deadlock", summaryText, mermaid, sb.ToString(), dlg.FileName);
                        Logger.Info($"死锁 HTML 报告成功保存至: {dlg.FileName}");
                        
                        if (MessageBox.Show("报告保存成功！是否立即在浏览器中打开？", "保存成功", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                        }
                    }
                }
                else if (MainTabControl.SelectedIndex == 1) // Execution Plan
                {
                    if (ViewModel.CurrentPlanDoc == null || string.IsNullOrEmpty(ViewModel.CurrentPlanFilePath))
                    {
                        MessageBox.Show("请先打开并分析一个执行计划文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string mermaid = ExecutionPlanVisualizer.GenerateMermaidPlan(ViewModel.CurrentPlanDoc, _showplanNs);
                    string warningsText = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(ViewModel.CurrentPlanDoc, _showplanNs);
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ViewModel.MissingIndexes.Clear();
                        var mis = PlanDiagnosticAnalyzer.ExtractMissingIndexes(ViewModel.CurrentPlanDoc, _showplanNs);
                        foreach (var m in mis)
                        {
                            ViewModel.MissingIndexes.Add(m);
                        }
                    });
                    
                    string formattedDiagnosis = warningsText
                        .Replace("\n\n", "<br/><br/>")
                        .Replace("【", "<h4 style='color:#0066cc;margin-bottom:5px;'>【")
                        .Replace("】", "】</h4>")
                        .Replace("👉", "✔️")
                        .Replace("🔥", "🔥")
                        .Replace("🚨", "🚨")
                        .Replace("⚠️", "⚠️");

                    string summaryText = $"执行计划文件: {Path.GetFileName(ViewModel.CurrentPlanFilePath)}\n";
                    var queryPlans = ViewModel.CurrentPlanDoc.Descendants(_showplanNs + "QueryPlan").ToList();
                    if (queryPlans.Count > 0)
                    {
                        summaryText += $"估算总成本: {queryPlans[0].Attribute("EstimatedTotalSubtreeCost")?.Value ?? "N/A"}\n";
                    }

                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "HTML 报告 (*.html)|*.html",
                        FileName = $"ExecutionPlanReport_{Path.GetFileNameWithoutExtension(ViewModel.CurrentPlanFilePath)}.html",
                        Title = "保存执行计划分析报告"
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        HtmlReportGenerator.SaveReport(ViewModel.CurrentPlanFilePath, "ExecutionPlan", summaryText, mermaid, formattedDiagnosis, dlg.FileName);
                        Logger.Info($"执行计划 HTML 报告成功保存至: {dlg.FileName}");
                        
                        if (MessageBox.Show("报告保存成功！是否立即在浏览器中打开？", "保存成功", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                        }
                    }
                }
                else
                {
                    MessageBox.Show("当前没有选中的分析标签！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("GenerateHtmlReport_Click", ex);
                MessageBox.Show($"生成 HTML 报告失败: {ex.Message}\n\n详细错误已记录到日志。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToPdf_Click(object sender, RoutedEventArgs e)
        {
            ExportReport("pdf", "PDF 报告 (*.pdf)|*.pdf");
        }

        private void ExportToWord_Click(object sender, RoutedEventArgs e)
        {
            ExportReport("docx", "Word 报告 (*.docx)|*.docx");
        }

        private string? CaptureElementToImage(FrameworkElement element)
        {
            try
            {
                if (element.ActualWidth == 0 || element.ActualHeight == 0) return null;

                double width = element.ActualWidth;
                double height = element.ActualHeight;

                System.Windows.Media.Imaging.RenderTargetBitmap rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Round(width), (int)Math.Round(height),
                    96d, 96d,
                    System.Windows.Media.PixelFormats.Pbgra32);

                System.Windows.Media.DrawingVisual dv = new System.Windows.Media.DrawingVisual();
                using (System.Windows.Media.DrawingContext ctx = dv.RenderOpen())
                {
                    ctx.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, width, height));
                    ctx.DrawRectangle(new System.Windows.Media.VisualBrush(element), null, new Rect(0, 0, width, height));
                }
                rtb.Render(dv);

                System.Windows.Media.Imaging.PngBitmapEncoder encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                string tempFile = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer_Graph_{Guid.NewGuid()}.png");
                using (var fs = new FileStream(tempFile, FileMode.Create))
                {
                    encoder.Save(fs);
                }
                return tempFile;
            }
            catch (Exception ex)
            {
                Logger.LogException("图片捕获失败", ex);
                return null;
            }
        }

        private void ExportReport(string extension, string filter)
        {
            try
            {
                string title = "";
                string content = "";
                string defaultFileName = "";
                string? tempImagePath = null;

                if (MainTabControl.SelectedIndex == 0)
                {
                    if (ViewModel.CurrentDeadlockDoc == null || ViewModel.CurrentDeadlockFilePath == null)
                    {
                        MessageBox.Show("当前没有加载的死锁文件可导出！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    title = "SQL Server 死锁深度诊断报告";
                    
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("=== 死锁类型与启发式诊断 ===");
                    var patterns = DeadlockPatternsListBox.ItemsSource as System.Collections.IEnumerable;
                    bool hasPatterns = false;
                    if (patterns != null)
                    {
                        foreach (var item in patterns)
                        {
                            if (item is DeadlockPattern p)
                            {
                                sb.AppendLine($"🔴 {p.TypeName}");
                                sb.AppendLine($"   【描述】: {p.Description}");
                                sb.AppendLine($"   🔍 【可能原因】: {p.LikelyCause}");
                                sb.AppendLine($"   💡 【建议】: {p.Recommendation}");
                                sb.AppendLine();
                                hasPatterns = true;
                            }
                        }
                    }
                    if (!hasPatterns)
                    {
                        sb.AppendLine("未检测到典型的已知死锁模式。");
                        sb.AppendLine();
                    }

                    if (!string.IsNullOrWhiteSpace(ViewModel.DeadlockPatternText))
                    {
                        sb.AppendLine("=== 分析与选中项详情 ===");
                        // Remove common emojis to prevent PDF generation engine (QuestPDF) from failing to render the text block
                        string safeText = ViewModel.DeadlockPatternText
                            .Replace("💀", "")
                            .Replace("🗄️", "")
                            .Replace("🔍", "")
                            .Replace("💡", "")
                            .Replace("🔴", "")
                            .Replace("🟢", "")
                            .Replace("🟠", "")
                            .Replace("📋", "");
                        sb.AppendLine(safeText);
                    }

                    content = sb.ToString();
                    defaultFileName = $"DeadlockReport_{Path.GetFileNameWithoutExtension(ViewModel.CurrentDeadlockFilePath)}.{extension}";
                    
                    // Capture Deadlock Canvas Border
                    tempImagePath = CaptureElementToImage(DeadlockCanvasBorder);
                }
                else if (MainTabControl.SelectedIndex == 1)
                {
                    if (string.IsNullOrWhiteSpace(ViewModel.PlanWarningsText) || ViewModel.CurrentPlanFilePath == null)
                    {
                        MessageBox.Show("当前没有执行计划诊断结果可导出！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    title = "SQL Server 执行计划专家诊断报告";
                    content = ViewModel.PlanWarningsText;
                    defaultFileName = $"PlanReport_{Path.GetFileNameWithoutExtension(ViewModel.CurrentPlanFilePath)}.{extension}";
                }
                else
                {
                    return;
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = filter,
                    FileName = defaultFileName,
                    Title = $"保存 {extension.ToUpper()} 分析报告"
                };

                if (dlg.ShowDialog() == true)
                {
                    if (extension == "pdf")
                    {
                        Core.Services.ReportExportService.ExportToPdf(dlg.FileName, title, content, tempImagePath);
                    }
                    else if (extension == "docx")
                    {
                        Core.Services.ReportExportService.ExportToWord(dlg.FileName, title, content, tempImagePath);
                    }
                    MessageBox.Show($"{extension.ToUpper()} 报告已成功导出！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Cleanup temp image
                if (!string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
                {
                    try { File.Delete(tempImagePath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException($"ExportTo{extension.ToUpper()}_Click", ex);
                MessageBox.Show($"导出失败:\n{ex.Message}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyAnalysisResult_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string textToCopy = "";
                if (MainTabControl.SelectedIndex == 0)
                {
                    if (string.IsNullOrWhiteSpace(ViewModel.DeadlockPatternText))
                    {
                        MessageBox.Show("当前没有死锁诊断结果可复制！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    textToCopy = "=== SQL Server 死锁诊断报告 ===\r\n\r\n" + ViewModel.DeadlockPatternText;
                }
                else if (MainTabControl.SelectedIndex == 1)
                {
                    if (string.IsNullOrWhiteSpace(ViewModel.PlanWarningsText))
                    {
                        MessageBox.Show("当前没有执行计划诊断结果可复制！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    textToCopy = "=== SQL Server 执行计划诊断报告 ===\r\n\r\n" + ViewModel.PlanWarningsText;
                }
                
                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);
                    MessageBox.Show("诊断结果已成功复制到剪贴板！", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败:\n{ex.Message}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearResults_Click(object sender, RoutedEventArgs e)
        {
            // 清空 ViewModel
            ViewModel.ClearResults();
            
            // 清空主要控件
            DeadlockGraphCanvas.Children.Clear();
            DeadlockProcessesList.ItemsSource = null;
            DeadlockResourcesList.ItemsSource = null;
            DeadlockPatternsListBox.ItemsSource = null;
            PlanOperatorTree.Items.Clear();
            DeadlockPatternsListBox.Items.Clear();
            StatusTextBlock.Text = "结果已清空";
        }

        private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            string logsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
            if (Directory.Exists(logsPath))
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", logsPath);
                }
                catch (Exception ex)
                {
                    Logger.LogException("OpenLogsFolder_Click", ex);
                    MessageBox.Show($"无法打开日志文件夹: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("日志目录尚未创建。");
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("SqlXmlAnalyzer 专业图形界面版 v2.0\n\n" +
                           "原控制台版本的增强 GUI 实现\n" +
                           "支持死锁 Wait-For Graph 可视化 + 执行计划树可视化\n\n" +
                           "开发中...", "关于", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        #endregion

        #region 事件处理 (补充)

        private void DeadlockProcessesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeadlockProcessesList.SelectedItem is DeadlockProcess proc)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"🔴 选中进程 (SPID {proc.Spid}) 详情：");
                sb.AppendLine($"----------------------------------------");
                sb.AppendLine($"标识 ID: {proc.Id}");
                sb.AppendLine($"当前状态: {proc.Status} | 隔离级别: {proc.Isolationlevel}");
                sb.AppendLine($"事务名称: {(!string.IsNullOrEmpty(proc.TransactionName) ? proc.TransactionName : "无")}");
                sb.AppendLine($"运行数据库: {(!string.IsNullOrEmpty(proc.CurrentDbName) ? proc.CurrentDbName : "Unknown")}");
                sb.AppendLine($"登录账号: {proc.Loginname} | 客户端主机: {proc.Hostname}");
                if (!string.IsNullOrEmpty(proc.ClientApp))
                    sb.AppendLine($"应用程序: {proc.ClientApp}");
                if (!string.IsNullOrEmpty(proc.WaitResource))
                    sb.AppendLine($"等待资源: {proc.WaitResource}");
                if (!string.IsNullOrEmpty(proc.WaitTime))
                    sb.AppendLine($"等待时间: {proc.WaitTime} ms");
                sb.AppendLine();
                sb.AppendLine($"📝 正在执行的 SQL 语句 (inputbuf):");
                sb.AppendLine(proc.Inputbuf);

                if (proc.ExecutionStack.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"🥞 执行堆栈 (Execution Stack):");
                    foreach (var frame in proc.ExecutionStack)
                    {
                        sb.AppendLine($"  • 过程: {frame.Procname} | 行号: {frame.Line}");
                        if (!string.IsNullOrEmpty(frame.Statement))
                            sb.AppendLine($"    SQL: {frame.Statement}");
                    }
                }

                // 进行 SARGability & 索引友好度智能扫描 ( 联动性能检测 )
                var sargWarnings = SargAnalyzer.Analyze(proc.Inputbuf);
                if (proc.ExecutionStack.Count > 0)
                {
                    foreach (var frame in proc.ExecutionStack)
                    {
                        if (!string.IsNullOrEmpty(frame.Statement))
                        {
                            var frameWarns = SargAnalyzer.Analyze(frame.Statement);
                            sargWarnings.AddRange(frameWarns);
                        }
                    }
                }
                sargWarnings = sargWarnings.GroupBy(w => w.Title).Select(g => g.First()).ToList();

                if (sargWarnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"⚡ SQL 语句性能与 SARG 扫描预警（DEADLOCK.py 专家级建议）：");
                    sb.AppendLine($"========================================================================");
                    foreach (var warn in sargWarnings)
                    {
                        sb.AppendLine($"【问题标题】 {warn.Title}");
                        sb.AppendLine($"【物理成因】 {warn.Desc}");
                        sb.AppendLine($"【解决方案】 {warn.Solution}");
                        sb.AppendLine($"------------------------------------------------------------------------");
                    }
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine($"💚 SQL 扫描通过：未检测到明显的前导模糊、函数致盲或负向查询等 SARG 索引致盲缺陷。");
                }

                ViewModel.DeadlockPatternText = sb.ToString();
            }
        }

        private void DeadlockResourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeadlockResourcesList.SelectedItem is LockResource res)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"🔑 涉及资源 ({res.LockType.ToUpperInvariant()}) 详情：");
                sb.AppendLine($"----------------------------------------");
                sb.AppendLine($"数据库 ID (DBID): {res.Dbid}");
                sb.AppendLine($"对象名称: {res.ObjectName}");
                if (!string.IsNullOrEmpty(res.IndexName))
                    sb.AppendLine($"关联索引: {res.IndexName}");
                sb.AppendLine($"HOBT ID: {res.Hobtid}");
                sb.AppendLine();
                sb.AppendLine($"✅ 持有该资源的进程 (Owners):");
                foreach (var owner in res.Owners)
                {
                    sb.AppendLine($"  • 标识 ID: {owner.Id}   模式 (Mode): {owner.Mode}");
                }
                sb.AppendLine();
                sb.AppendLine($"⏳ 等待该资源的进程 (Waiters):");
                foreach (var waiter in res.Waiters)
                {
                    sb.AppendLine($"  • 标识 ID: {waiter.Id}   请求模式 (Mode): {waiter.Mode}  类型: {waiter.RequestType}");
                }

                ViewModel.DeadlockPatternText = sb.ToString();
            }
        }

        private void ToggleLeft_Click(object sender, RoutedEventArgs e)
        {
            if (DeadlockLeftColumn == null) return;
            if (DeadlockLeftColumn.Width.Value > 0)
            {
                DeadlockLeftColumn.Width = new GridLength(0);
                ToggleLeftBtn.Content = "▶ 侧边栏";
            }
            else
            {
                DeadlockLeftColumn.Width = new GridLength(280);
                ToggleLeftBtn.Content = "◀ 侧边栏";
            }
        }

        private void ToggleRight_Click(object sender, RoutedEventArgs e)
        {
            if (DeadlockRightColumn == null) return;
            if (DeadlockRightColumn.Width.Value > 0)
            {
                DeadlockRightColumn.Width = new GridLength(0);
                ToggleRightBtn.Content = "◀ 属性栏";
            }
            else
            {
                DeadlockRightColumn.Width = new GridLength(320);
                ToggleRightBtn.Content = "属性栏 ▶";
            }
        }

        private void ZoomToFitDeadlock_Click(object sender, RoutedEventArgs e)
        {
            DoZoomToFitDeadlock();
        }

        private void DoZoomToFitDeadlock()
        {
            if (_nodePositions.Count == 0) return;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            double procW = 220, procH = 90;
            double resW = 160, resH = 50;

            foreach (var kp in _nodePositions)
            {
                string id = kp.Key;
                Point pos = kp.Value;
                bool isResource = id.StartsWith("res_");
                double w = isResource ? resW : procW;
                double h = isResource ? resH : procH;

                if (pos.X < minX) minX = pos.X;
                if (pos.X + w > maxX) maxX = pos.X + w;
                if (pos.Y < minY) minY = pos.Y;
                if (pos.Y + h > maxY) maxY = pos.Y + h;
            }

            double margin = 60;
            double contentW = (maxX - minX) + margin * 2;
            double contentH = (maxY - minY) + margin * 2;

            double viewW = DeadlockCanvasBorder.ActualWidth > 0 ? DeadlockCanvasBorder.ActualWidth : 800;
            double viewH = DeadlockCanvasBorder.ActualHeight > 0 ? DeadlockCanvasBorder.ActualHeight : 600;

            double scaleX = viewW / contentW;
            double scaleY = viewH / contentH;
            double scale = Math.Min(scaleX, scaleY);

            if (scale < 0.2) scale = 0.2;
            if (scale > 2.0) scale = 2.0;

            DeadlockScaleTransform.ScaleX = scale;
            DeadlockScaleTransform.ScaleY = scale;

            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;

            DeadlockTranslateTransform.X = viewW / 2 - centerX * scale;
            DeadlockTranslateTransform.Y = viewH / 2 - centerY * scale;
        }

        private void DeadlockPatternsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeadlockPatternsListBox.SelectedItem is DeadlockPattern pattern)
            {
                ViewModel.DeadlockPatternText = 
                    $"类型: {pattern.TypeName}\n\n" +
                    $"描述: {pattern.Description}\n\n" +
                    $"可能原因: {pattern.LikelyCause}\n\n" +
                    $"推荐措施: {pattern.Recommendation}";
            }
        }

        #region 折叠面板事件处理
        private GridLength _leftColWidth = new GridLength(320);
        private GridLength _rightColWidth = new GridLength(280);

        private void LeftPanel_Expanded(object sender, RoutedEventArgs e)
        {
            if (PlanContentGrid != null && PlanContentGrid.ColumnDefinitions.Count > 0)
                PlanContentGrid.ColumnDefinitions[0].Width = _leftColWidth;
        }

        private void LeftPanel_Collapsed(object sender, RoutedEventArgs e)
        {
            if (PlanContentGrid != null && PlanContentGrid.ColumnDefinitions.Count > 0)
            {
                _leftColWidth = PlanContentGrid.ColumnDefinitions[0].Width;
                PlanContentGrid.ColumnDefinitions[0].Width = GridLength.Auto;
            }
        }

        private void RightPanel_Expanded(object sender, RoutedEventArgs e)
        {
            if (PlanContentGrid != null && PlanContentGrid.ColumnDefinitions.Count > 4)
                PlanContentGrid.ColumnDefinitions[4].Width = _rightColWidth;
        }

        private void RightPanel_Collapsed(object sender, RoutedEventArgs e)
        {
            if (PlanContentGrid != null && PlanContentGrid.ColumnDefinitions.Count > 4)
            {
                _rightColWidth = PlanContentGrid.ColumnDefinitions[4].Width;
                PlanContentGrid.ColumnDefinitions[4].Width = GridLength.Auto;
            }
        }
        #endregion


        private void PlanOperatorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is XElement relOp)
            {
                PlanPropertiesGrid.ItemsSource = GetGroupedProperties(relOp);
            }
        }

        private void RefreshDeadlockGraph_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ViewModel.CurrentDeadlockFilePath))
            {
                MessageBox.Show("没有已加载的死锁文件，无法刷新。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AnalyzeFile(ViewModel.CurrentDeadlockFilePath);
        }

        private void CopyDeadlockMermaid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel.CurrentDeadlockDoc == null)
                {
                    MessageBox.Show("当前没有加载的死锁文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var (processes, resources, victimId) = DeadlockXmlParser.ParseDeadlockXml(ViewModel.CurrentDeadlockDoc);
                var graph = DeadlockGraphBuilder.Build(processes, resources, victimId);
                string mermaid = DeadlockGraphBuilder.GenerateMermaid(graph, true);
                
                Clipboard.SetText(mermaid);
                MessageBox.Show("死锁 Mermaid 代码已成功复制到剪贴板！", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
                Logger.Info("已成功将死锁 Mermaid 代码复制到剪贴板。");
            }
            catch (Exception ex)
            {
                Logger.LogException("CopyDeadlockMermaid", ex);
                MessageBox.Show($"复制 Mermaid 代码失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshPlanGraph_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ViewModel.CurrentPlanFilePath))
            {
                MessageBox.Show("没有已加载的执行计划文件，无法刷新。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AnalyzeFile(ViewModel.CurrentPlanFilePath);
        }

        private void CopyPlanMermaid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel.CurrentPlanDoc == null)
                {
                    MessageBox.Show("当前没有加载的执行计划文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string mermaid = ExecutionPlanVisualizer.GenerateMermaidPlan(ViewModel.CurrentPlanDoc, _showplanNs);
                Clipboard.SetText(mermaid);
                MessageBox.Show("执行计划 Mermaid 代码已成功复制到剪贴板！", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
                Logger.Info("已成功将执行计划 Mermaid 代码复制到剪贴板。");
            }
            catch (Exception ex)
            {
                Logger.LogException("CopyPlanMermaid", ex);
                MessageBox.Show($"复制 Mermaid 代码失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlanVisualTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is PlanVisualNode node && node.Tag is XElement relOp)
            {
                PlanPropertiesGrid.ItemsSource = GetGroupedProperties(relOp);
            }
        }

        // Nodify 节点选中 -> 同步到主右侧属性面板 (Plan Explorer 风格)
        private void PlanNodifyGraph_NodeSelected(object sender, PlanNodeViewModel node)
        {
            if (node == null) return;

            var props = new List<KeyValuePair<string, string>>
            {
                new("Physical Operator", node.PhysicalOp),
                new("Logical Operator", node.LogicalOp),
                new("Subtree Cost", node.Cost.ToString("F4")),
                new("Cost % (heuristic)", node.CostPercent + "%"),
                new("Est. Rows", node.EstRows),
                new("Actual Rows", string.IsNullOrEmpty(node.ActualRows) ? "N/A (estimated only)" : node.ActualRows),
                new("Object", string.IsNullOrEmpty(node.ObjectDetails) ? "(无对象引用)" : node.ObjectDetails),
                new("Parallel", node.IsParallel ? "是" : "否"),
            };
            if (!string.IsNullOrEmpty(node.Warnings))
                props.Add(new("Warnings", node.Warnings));

            PlanPropertiesGrid.ItemsSource = props;
        }

        private void PlanNodifyGraph_NodeDoubleClicked(object sender, PlanNodeViewModel node)
        {
            if (node == null || node.RawElement == null) return;
            PlanPropertiesGrid.ItemsSource = GetGroupedProperties(node.RawElement);
        }

        private System.Windows.Data.ListCollectionView GetGroupedProperties(XElement relOp)
        {
            var propertyItems = new List<PropertyItem>();

            var map = new Dictionary<string, (string Group, string Name)>
            {
                { "NodeId", ("杂项", "节点 ID") },
                { "PhysicalOp", ("所有执行的实际行数", "物理运算") },
                { "LogicalOp", ("杂项", "逻辑操作") },
                { "EstimateRows", ("所有执行的估计行数", "每个执行的估计行数") },
                { "EstimateIO", ("杂项", "估计 I/O 开销") },
                { "EstimateCPU", ("杂项", "估计 CPU 开销") },
                { "AvgRowSize", ("杂项", "估计行大小") },
                { "EstimatedTotalSubtreeCost", ("杂项", "估计子树大小") },
                { "EstimateRebinds", ("杂项", "估计的重新绑定次数") },
                { "EstimateRewinds", ("杂项", "估计的重绕次数") },
                { "EstimatedExecutionMode", ("杂项", "估计的执行模式") },
                { "Parallel", ("杂项", "并行") },
                { "ActualExecutionMode", ("实际时间统计信息", "实际执行模式") }
            };

            foreach (var attr in relOp.Attributes())
            {
                string key = attr.Name.LocalName;
                if (map.TryGetValue(key, out var translation))
                {
                    propertyItems.Add(new PropertyItem { Group = translation.Group, Name = translation.Name, Value = attr.Value });
                }
                else
                {
                    propertyItems.Add(new PropertyItem { Group = "杂项", Name = key, Value = attr.Value });
                }
            }

            foreach (var child in relOp.Elements())
            {
                if (child.Name.LocalName == "OutputList")
                {
                    foreach(var col in child.Descendants(child.Name.Namespace + "ColumnReference"))
                    {
                        string db = col.Attribute("Database")?.Value ?? "";
                        string schema = col.Attribute("Schema")?.Value ?? "";
                        string table = col.Attribute("Table")?.Value ?? "";
                        string column = col.Attribute("Column")?.Value ?? "";
                        propertyItems.Add(new PropertyItem { Group = "输出列表", Name = $"[{db}].[{schema}].[{table}].{column}", Value = "" });
                    }
                }
                else if (child.Name.LocalName == "RunTimeInformation")
                {
                    foreach (var rt in child.Elements())
                    {
                        foreach (var attr in rt.Attributes())
                        {
                            string g = "所有执行的实际行数";
                            string n = attr.Name.LocalName;
                            if (n == "ActualRows") n = "所有执行的实际行数";
                            propertyItems.Add(new PropertyItem { Group = g, Name = n, Value = attr.Value });
                        }
                    }
                }
                else if (child.Name.LocalName != "RelOp")
                {
                    foreach (var attr in child.Attributes())
                    {
                        propertyItems.Add(new PropertyItem { Group = child.Name.LocalName, Name = attr.Name.LocalName, Value = attr.Value });
                    }
                }
            }

            var view = new System.Windows.Data.ListCollectionView(propertyItems);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Group"));
            return view;
        }

        public class PropertyItem
        {
            public string Group { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
        }

        private void OpenPlanMermaidInBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentPlanDoc != null)
            {
                string mermaid = ExecutionPlanVisualizer.GenerateMermaidPlan(ViewModel.CurrentPlanDoc, _showplanNs);
                OpenMermaidInBrowser(mermaid);
            }
        }

        private void OpenDeadlockMermaidInBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel.CurrentDeadlockDoc == null)
                {
                    MessageBox.Show("当前没有加载的死锁文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var (processes, resources, victimId) = DeadlockXmlParser.ParseDeadlockXml(ViewModel.CurrentDeadlockDoc);
                var graph = DeadlockGraphBuilder.Build(processes, resources, victimId);
                string mermaid = DeadlockGraphBuilder.GenerateMermaid(graph, true);
                OpenMermaidInBrowser(mermaid);
                Logger.Info("已在浏览器中打开死锁 Mermaid 等待图。");
            }
            catch (Exception ex)
            {
                Logger.LogException("OpenDeadlockMermaidInBrowser", ex);
                MessageBox.Show($"在浏览器中打开 Mermaid 图形失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenMermaidInBrowser(string mermaidCode)
        {
            string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><script src='https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js'></script></head>
<body><div class='mermaid'>{System.Net.WebUtility.HtmlEncode(mermaidCode)}</div>
<script>mermaid.initialize({{startOnLoad:true}});mermaid.run();</script></body></html>";

            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SqlXmlAnalyzer_Mermaid.html");
            File.WriteAllText(tempFile, html);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempFile) { UseShellExecute = true });
        }

        #endregion

        private void PlanNodifyGraph_Loaded(object sender, RoutedEventArgs e)
        {

        }

        // --- 调优历史与 A/B 并排对比事件处理器 ---
        private void TuningHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TuningHistoryListView.SelectedItem is Core.ViewModels.PlanSnapshot snapshot)
            {
                AnalyzeExecutionPlanDocument(snapshot.Document, snapshot.FilePath);
            }
        }

        private void SaveSession_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "SqlXmlAnalyzer 调优会话 (*.pesession)|*.pesession",
                Title = "保存当前调优会话",
                FileName = "Tuning_Session.pesession"
            };
            if (dlg.ShowDialog() == true)
            {
                ViewModel.SaveSession(dlg.FileName);
            }
        }

        private void LoadSession_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SqlXmlAnalyzer 调优会话 (*.pesession)|*.pesession",
                Title = "打开调优会话文件"
            };
            if (dlg.ShowDialog() == true)
            {
                ViewModel.LoadSession(dlg.FileName);
            }
        }

        private void SwapPlanAB_Click(object sender, RoutedEventArgs e)
        {
            var temp = ViewModel.PlanA;
            ViewModel.PlanA = ViewModel.PlanB;
            ViewModel.PlanB = temp;
        }
    }
}
