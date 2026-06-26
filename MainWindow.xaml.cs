using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Abstractions;
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
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle).AddHook(new HwndSourceHook(WindowProc));
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0024) // WM_GETMINMAXINFO
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                GetMonitorInfo(monitor, ref monitorInfo);

                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
                mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);

                mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        public Core.ViewModels.MainViewModel ViewModel { get; }
        private readonly Core.XelReader _xelReader;
        private readonly TemporaryFileManager _temporaryFileManager;
        private readonly Core.Services.AnalysisSessionCoordinator _analysisSessions;
        private readonly Core.Services.BrowserLauncher _browserLauncher;
        private readonly Core.Services.PdfWordReportService _pdfWordReportService;
        private readonly Core.Services.DocumentOpenService _documentOpenService;
        private readonly Core.Services.DeadlockDocumentController _deadlockDocumentController;
        private readonly Core.Services.PlanDocumentController _planDocumentController;
        private readonly Core.Services.PlanComparisonController _planComparisonController;
        private readonly Core.Services.IFileDialogService _fileDialogService;

        private Dictionary<string, FrameworkElement> _nodeElements = new Dictionary<string, FrameworkElement>();
        private Dictionary<(string, string), (System.Windows.Shapes.Line line, System.Windows.Shapes.Polygon arrowHead, Border label)> _arrowCache = new Dictionary<(string, string), (System.Windows.Shapes.Line line, System.Windows.Shapes.Polygon arrowHead, Border label)>();
        private List<(string fromId, string toId, string label)> _edgesForDrawing = new List<(string, string, string)>();

        private DeadlockTimelineParser.ParsedDeadlock? _currentTimeline;
        private DeadlockPlaybackViewModel? _playbackViewModel;
        private Dictionary<(string, string), Border> _stepBadges = new Dictionary<(string, string), Border>();

        private string _currentOriginalSql = "";
        private string _currentRefactoredSql = "";
        private ScrollViewer? _originalScroll;
        private ScrollViewer? _refactoredScroll;
        private bool _isSynchronizingScroll = false;

        private static readonly TextDecorationCollection SquigglyUnderline = CreateSquigglyUnderline();

        private static TextDecorationCollection CreateSquigglyUnderline()
        {
            var brush = new DrawingBrush();
            brush.Viewport = new Rect(0, 0, 6, 4);
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.TileMode = TileMode.Tile;

            var geometry = new GeometryGroup();
            var path = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(0, 2) };
            figure.Segments.Add(new BezierSegment(new Point(1.5, 0), new Point(1.5, 4), new Point(3, 2), true));
            figure.Segments.Add(new BezierSegment(new Point(4.5, 0), new Point(4.5, 4), new Point(6, 2), true));
            path.Figures.Add(figure);

            var drawing = new GeometryDrawing(null, new Pen(Brushes.Red, 1.2), path);
            brush.Drawing = drawing;

            var dec = new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Pen = new Pen(brush, 3)
            };

            var decs = new TextDecorationCollection();
            decs.Add(dec);
            return decs;
        }



        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (this.WindowState == System.Windows.WindowState.Maximized)
                    this.WindowState = System.Windows.WindowState.Normal;
                else
                    this.WindowState = System.Windows.WindowState.Maximized;
            }
            else
            {
                this.DragMove();
            }
        }

        private void Minimize_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.WindowState = System.Windows.WindowState.Minimized;
        }

        private void Maximize_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (this.WindowState == System.Windows.WindowState.Maximized)
                this.WindowState = System.Windows.WindowState.Normal;
            else
                this.WindowState = System.Windows.WindowState.Maximized;
        }

        private void Close_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.Close();
        }

        private void ThemeToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            if (theme == null) return;
            theme.SetBaseTheme(ThemeToggle.IsChecked == true ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);
        }

        public MainWindow(
            ApplicationOrchestrator orchestrator,
            IFileHandler fileHandler,
            Core.XelReader? xelReader = null,
            TemporaryFileManager? temporaryFileManager = null,
            Core.Services.AnalysisSessionCoordinator? analysisSessions = null,
            Core.Services.BrowserLauncher? browserLauncher = null,
            Core.Services.PdfWordReportService? pdfWordReportService = null,
            Core.Services.DocumentOpenService? documentOpenService = null,
            Core.Services.DeadlockDocumentController? deadlockDocumentController = null,
            Core.Services.PlanDocumentController? planDocumentController = null,
            Core.Services.PlanComparisonController? planComparisonController = null,
            Core.Services.IFileDialogService? fileDialogService = null,
            DeadlockAnalysisService? deadlockAnalysisService = null,
            Core.Services.PlanAnalysisService? planAnalysisService = null)
        {
            InitializeComponent();
            _xelReader = xelReader ?? new Core.XelReader();
            _temporaryFileManager = temporaryFileManager ?? new TemporaryFileManager();
            _analysisSessions = analysisSessions ?? new Core.Services.AnalysisSessionCoordinator();
            _browserLauncher = browserLauncher ?? new Core.Services.BrowserLauncher(_temporaryFileManager);
            _pdfWordReportService = pdfWordReportService
                ?? new Core.Services.PdfWordReportService(_temporaryFileManager);
            _documentOpenService = documentOpenService
                ?? new Core.Services.DocumentOpenService();
            DeadlockAnalysisService effectiveDeadlockAnalysisService =
                deadlockAnalysisService ?? new DeadlockAnalysisService();
            Core.Services.PlanAnalysisService effectivePlanAnalysisService =
                planAnalysisService ?? new Core.Services.PlanAnalysisService(
                    orchestrator,
                    fileHandler,
                    _temporaryFileManager);
            _deadlockDocumentController = deadlockDocumentController
                ?? new Core.Services.DeadlockDocumentController(effectiveDeadlockAnalysisService);
            _planDocumentController = planDocumentController
                ?? new Core.Services.PlanDocumentController(effectivePlanAnalysisService);
            _planComparisonController = planComparisonController
                ?? new Core.Services.PlanComparisonController();
            _fileDialogService = fileDialogService
                ?? new Core.Services.WpfFileDialogService();
            _temporaryFileManager.CleanupStaleFiles(TimeSpan.FromHours(24));
            ViewModel = new Core.ViewModels.MainViewModel();
            ViewModel.ShowMessageBox = msg => MessageBox.Show(msg);
            this.DataContext = ViewModel;
            SetupCanvasZoomPan();
            this.Loaded += (s, e) => SetupSynchronizedScrolling();
            this.Closed += (s, e) => _analysisSessions.CancelCurrent();

            // 监听 PlanA / PlanB 快照变化，动态重构并排对比的操作符结构树
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.PlanA) || e.PropertyName == nameof(ViewModel.PlanB))
                {
                    RefreshABCompareTrees();
                    if (ViewModel.PlanA != null && ViewModel.PlanB != null)
                    {
                        ViewModel.ActivateWorkspace(Core.ViewModels.WorkspaceMode.Compare);
                        var tab = MainTabControl.Items.OfType<System.Windows.Controls.TabItem>().FirstOrDefault(t => t.Header?.ToString()?.Contains("A/B") == true);
                        if (tab != null) MainTabControl.SelectedItem = tab;
                    }
                }
            };
        }

        #region 文件打开

        private string? ShowOpenFileDialog(
            string filter,
            string title,
            string? defaultExtension = null,
            string? fileName = null)
        {
            return _fileDialogService.ShowOpenFile(
                new Core.Services.FileDialogRequest(
                    filter,
                    title,
                    defaultExtension,
                    fileName));
        }

        private string? ShowSaveFileDialog(
            string filter,
            string title,
            string? defaultExtension = null,
            string? fileName = null)
        {
            return _fileDialogService.ShowSaveFile(
                new Core.Services.FileDialogRequest(
                    filter,
                    title,
                    defaultExtension,
                    fileName));
        }

        private async void OpenDeadlockFile_Click(object sender, RoutedEventArgs e)
        {
            string? fileName = ShowOpenFileDialog(
                "Deadlock files (*.xml;*.xdl;*.xel)|*.xml;*.xdl;*.xel|All files (*.*)|*.*",
                "Open deadlock report");

            if (fileName != null)
            {
                string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                if (ext == ".xel")
                {
                    await AnalyzeXelFileAsync(fileName);
                }
                else
                {
                    AnalyzeDeadlockFile(fileName);
                }
            }
        }

        private async Task AnalyzeXelFileAsync(string filePath)
        {
            var session = _analysisSessions.Begin();
            try
            {
                var reports = await _xelReader.ReadDeadlocksAsync(filePath, session.Token);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }
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
            catch (OperationCanceledException)
            {
                Logger.Verbose($"XEL 分析已取消: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }
                Logger.LogException("MainWindow.AnalyzeXelFileAsync", ex);
                MessageBox.Show("解析 XEL 文件时发生错误: " + ex.Message);
            }
        }

        private async void XelDeadlockSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (XelDeadlockSelector.SelectedItem is Core.XelDeadlockReport report)
            {
                try
                {
                    await AnalyzeDeadlockXmlAsync(
                        report.DeadlockXml,
                        $"XEL 死锁事件 {report.Timestamp}");
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
            string? fileName = ShowOpenFileDialog(
                "Execution plan files (*.sqlplan;*.xml)|*.sqlplan;*.xml|All files (*.*)|*.*",
                "Open execution plan");

            if (fileName != null)
            {
                AnalyzeExecutionPlanFile(fileName);
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
            _ = AnalyzeFileAsync(filePath);
        }

        private void AnalyzeExecutionPlanFile(string filePath)
        {
            _ = AnalyzeFileAsync(filePath);
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
                var doc = SafeXmlHelper.LoadSafe(filePath);
                Logger.Info("使用 SafeXmlHelper.LoadSafe 成功加载文件");
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
                    var doc = SafeXmlHelper.LoadSafe(sr);
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

        public async void AnalyzeFile(string filePath)
        {
            await AnalyzeFileAsync(filePath);
        }

        public async Task AnalyzeFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Error($"尝试分析不存在的文件: {filePath}");
                MessageBox.Show("指定的文件不存在或路径无效！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var session = _analysisSessions.Begin();
            try
            {
                StatusTextBlock.Text = $"正在加载并识别文件：{System.IO.Path.GetFileName(filePath)}...";
                Core.Services.DocumentOpenResult openResult =
                    await _documentOpenService.OpenAsync(filePath, session.Token);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                if (!openResult.IsSuccess)
                {
                    Logger.Error($"Document open failed: {filePath}. {openResult.ErrorMessage}");
                    MessageBox.Show("鎸囧畾鐨勬枃浠朵笉瀛樺湪鎴栬矾寰勬棤鏁堬紒", "閿欒", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusTextBlock.Text = "鏂囦欢鍔犺浇澶辫触";
                    return;
                }

                if (openResult.Kind == Core.Services.AnalysisDocumentKind.XelDeadlockTrace)
                {
                    await AnalyzeXelFileAsync(filePath);
                    return;
                }

                XDocument? doc = openResult.Document;
                if (doc == null)
                {
                    MessageBox.Show(
                        "The file did not produce an XML document.",
                        "File load failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    StatusTextBlock.Text = "File load failed";
                    return;
                }

                if (openResult.Kind == Core.Services.AnalysisDocumentKind.DeadlockXml)
                {
                    Logger.Info($"文件被识别为死锁报告: {filePath}");
                    ViewModel.CurrentDeadlockFilePath = filePath;
                    await AnalyzeDeadlockDocumentAsync(doc, filePath, session.RequestId, session.Token);
                }
                else if (openResult.Kind == Core.Services.AnalysisDocumentKind.ExecutionPlanXml)
                {
                    Logger.Info($"文件被识别为 SQL Server 执行计划: {filePath}");
                    ViewModel.CurrentPlanFilePath = filePath;
                    await AnalyzeExecutionPlanDocumentAsync(doc, filePath, session.RequestId, session.Token);
                }
                else
                {
                    Logger.Warning($"文件格式无法自动识别: {filePath}. 根节点 LocalName: {doc.Root?.Name.LocalName}, Namespace: {doc.Root?.Name.Namespace.NamespaceName}");
                    MessageBox.Show("无法自动识别该 XML 文件的类型！\n\n请确认该文件是标准的 SQL Server 死锁 XML（根节点为 <deadlock>）或执行计划 XML（根节点为 <ShowPlanXML>）。",
                                    "格式未识别", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusTextBlock.Text = "未知文件类型";
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"文件分析已取消: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }
                Logger.LogException("AnalyzeFile", ex);
                MessageBox.Show($"解析文件失败: {ex.Message}\n\n详细错误已记录到日志。", "分析错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "解析失败";
            }
        }

        private async Task AnalyzeDeadlockXmlAsync(string xml, string displayName)
        {
            var session = _analysisSessions.Begin();
            try
            {
                StatusTextBlock.Text = $"正在分析：{displayName}...";
                XDocument doc = await Task.Run(
                    () =>
                    {
                        session.Token.ThrowIfCancellationRequested();
                        XDocument parsed = SafeXmlHelper.ParseSafe(xml);
                        session.Token.ThrowIfCancellationRequested();
                        return parsed;
                    },
                    session.Token);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                ViewModel.CurrentDeadlockFilePath = displayName;
                await AnalyzeDeadlockDocumentAsync(doc, displayName, session.RequestId, session.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"内存死锁分析已取消: {displayName}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }
                Logger.LogException("AnalyzeDeadlockXmlAsync", ex);
                MessageBox.Show($"分析死锁内容失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "分析失败";
            }
        }

        private async Task AnalyzeDeadlockDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken)
        {
            try
            {
                StatusTextBlock.Text = $"正在分析死锁文件：{System.IO.Path.GetFileName(filePath)}...";

                Core.Services.DeadlockDocumentResult documentResult =
                    await _deadlockDocumentController.AnalyzeAsync(
                        doc,
                        filePath,
                        cancellationToken);
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }

                var result = documentResult.Analysis;
                ViewModel.CurrentDeadlockDoc = documentResult.Document;
                ViewModel.ActivateWorkspace(Core.ViewModels.WorkspaceMode.Deadlock);
                DeadlockProcessesList.ItemsSource = result.Processes;
                DeadlockResourcesList.ItemsSource = result.Resources;
                DeadlockPatternsListBox.ItemsSource = result.Patterns;

                _currentTimeline = result.Timeline;
                _playbackViewModel = new DeadlockPlaybackViewModel(_currentTimeline.Events);
                _playbackViewModel.StepChanged += (s, e) => UpdatePlaybackGraphVisibility();
                PlaybackControl.DataContext = _playbackViewModel;

                foreach (var b in _stepBadges.Values) { DeadlockGraphCanvas.Children.Remove(b); }
                _stepBadges.Clear();

                BuildDeadlockWaitForTree(result.Graph);

                UpdatePlaybackGraphVisibility();

                MainTabControl.SelectedIndex = 0;
                foreach (string warning in result.Warnings)
                {
                    Logger.Warning(warning);
                }

                StatusTextBlock.Text = result.Warnings.Count == 0
                    ? "死锁分析完成"
                    : $"死锁分析完成（{result.Warnings.Count} 条警告）";
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"死锁分析已取消: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }
                Logger.LogException("AnalyzeDeadlockDocument", ex);
                MessageBox.Show($"分析死锁内容失败: {ex.Message}\n\n完整错误已记录到日志文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "分析失败";
            }
        }

        private async Task AnalyzeExecutionPlanDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken)
        {
            try
            {
                StatusTextBlock.Text = $"正在分析执行计划：{System.IO.Path.GetFileName(filePath)}...";

                Core.Services.PlanDocumentResult documentResult =
                    await _planDocumentController.AnalyzeAsync(
                        doc,
                        filePath,
                        _showplanNs,
                        cancellationToken);
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }

                var result = documentResult.Analysis;
                ViewModel.CurrentPlanDoc = documentResult.Document;
                ViewModel.ActivateWorkspace(Core.ViewModels.WorkspaceMode.ExecutionPlan);
                Logger.Info($"[ExecutionPlan] 已生成 Mermaid 代码，长度: {result.Mermaid.Length} 字符");
                BuildPlanVisualTree(doc, _showplanNs);

                ViewModel.MissingIndexes.Clear();
                foreach (var mi in result.MissingIndexes)
                {
                    ViewModel.MissingIndexes.Add(mi);
                }

                PlanXmlTextBox.Text = result.DocumentText;
                PlanStatementTextBox.Text = result.QueryText.Length > 800
                    ? result.QueryText.Substring(0, 800) + "..."
                    : result.QueryText;
                _currentOriginalSql = result.QueryText;
                _currentRefactoredSql = result.RefactoredSql;
                UpdateSqlDiffViews();

                var tree = BuildPlanTreeView(doc, _showplanNs);
                PlanOperatorTree.Items.Clear();
                if (tree != null) PlanOperatorTree.Items.Add(tree);

                PlanWarningsTextBox.Text = result.WarningsText;

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

                try
                {
                    var paramList = doc.Descendants(_showplanNs + "ParameterList").Descendants(_showplanNs + "ColumnReference");
                    var sniffedParam = paramList.FirstOrDefault(p =>
                        !string.IsNullOrEmpty(p.Attribute("ParameterCompiledValue")?.Value) &&
                        !string.IsNullOrEmpty(p.Attribute("ParameterRuntimeValue")?.Value) &&
                        p.Attribute("ParameterCompiledValue")?.Value != p.Attribute("ParameterRuntimeValue")?.Value);

                    if (sniffedParam != null)
                    {
                        string col = sniffedParam.Attribute("Column")?.Value ?? "@Param";
                        string comp = sniffedParam.Attribute("ParameterCompiledValue")?.Value ?? "";
                        string run = sniffedParam.Attribute("ParameterRuntimeValue")?.Value ?? "";
                        StatisticsHistogramView.LoadParameterData(col, comp, run);
                    }
                    else
                    {
                        var firstParam = paramList.FirstOrDefault();
                        if (firstParam != null)
                        {
                            string col = firstParam.Attribute("Column")?.Value ?? "@Param";
                            string comp = firstParam.Attribute("ParameterCompiledValue")?.Value ?? "1";
                            string run = firstParam.Attribute("ParameterRuntimeValue")?.Value ?? "1";
                            StatisticsHistogramView.LoadParameterData(col, comp, run);
                        }
                    }
                    StatisticsHistogramView.LoadStatisticsUsage(doc, _showplanNs);
                }
                catch (Exception ex)
                {
                    Logger.LogException("Load Histogram", ex);
                }

                StatusTextBlock.Text = "执行计划分析完成";
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"执行计划分析已取消: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }
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
                                Width = 16,
                                Height = 16,
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
            var collapsedResources = resources.Select((r, idx) =>
            {
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
            bool toIsResource = toId.StartsWith("res_");

            double fromW = fromIsResource ? resW : procW;
            double fromH = fromIsResource ? resH : procH;
            double toW = toIsResource ? resW : procW;
            double toH = toIsResource ? resH : procH;

            Point fromTopLeft = _nodePositions.TryGetValue(fromId, out var fp) ? fp : new Point(80, 150);
            Point toTopLeft = _nodePositions.TryGetValue(toId, out var tp) ? tp : new Point(400, 150);

            Point fromCenter = new Point(fromTopLeft.X + fromW / 2, fromTopLeft.Y + fromH / 2);
            Point toCenter = new Point(toTopLeft.X + toW / 2, toTopLeft.Y + toH / 2);

            double dx = toCenter.X - fromCenter.X;
            double dy = toCenter.Y - fromCenter.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 0.1) dist = 0.1;
            double ux = dx / dist;
            double uy = dy / dist;

            double factorFrom = Math.Min((fromW / 2) / Math.Max(0.001, Math.Abs(ux)), (fromH / 2) / Math.Max(0.001, Math.Abs(uy)));
            double factorTo = Math.Min((toW / 2) / Math.Max(0.001, Math.Abs(ux)), (toH / 2) / Math.Max(0.001, Math.Abs(uy)));

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
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
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

        private void RefreshABCompareTrees()
        {
            PlanATreeView.Items.Clear();
            PlanBTreeView.Items.Clear();

            Core.Services.PlanComparisonResult comparison =
                _planComparisonController.BuildComparison(
                    ViewModel.PlanA,
                    ViewModel.PlanB,
                    _showplanNs);

            if (comparison.PlanA != null)
            {
                PlanATreeView.Items.Add(BuildDiffTreeView(comparison.PlanA, false));
            }

            if (comparison.PlanB != null)
            {
                PlanBTreeView.Items.Add(BuildDiffTreeView(comparison.PlanB, true));
            }
        }

        private TreeViewItem BuildDiffTreeView(
            Core.Services.PlanComparisonNode node,
            bool isPlanB)
        {
            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            var textBlockOp = new TextBlock
            {
                Text = GetComparisonOperatorText(node, isPlanB),
                FontWeight = FontWeights.SemiBold
            };
            var textBlockCost = new TextBlock
            {
                Text = $" (Cost: {node.Cost:F4})",
                Foreground = Brushes.Gray
            };

            Border border = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 4, 0)
            };

            ApplyComparisonStateStyle(node.State, isPlanB, border, textBlockOp);
            ApplyCostDeltaStyle(node, textBlockCost);

            stackPanel.Children.Add(textBlockOp);
            stackPanel.Children.Add(textBlockCost);

            if (node.RuntimeDeltas.Count > 0)
            {
                var textBlockRuntime = new TextBlock
                {
                    Text = " | " + string.Join(", ", node.RuntimeDeltas.Select(FormatRuntimeDelta)),
                    Foreground = isPlanB ? Brushes.Purple : Brushes.Teal,
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                stackPanel.Children.Add(textBlockRuntime);
            }

            border.Child = stackPanel;

            var item = new TreeViewItem
            {
                Header = border,
                Tag = node.Source,
                IsExpanded = true
            };

            foreach (Core.Services.PlanComparisonNode child in node.Children)
            {
                item.Items.Add(BuildDiffTreeView(child, isPlanB));
            }

            return item;
        }

        private static string GetComparisonOperatorText(
            Core.Services.PlanComparisonNode node,
            bool isPlanB)
        {
            return node.State switch
            {
                Core.Services.PlanComparisonNodeState.Added => $"{node.PhysicalOp} [Added]",
                Core.Services.PlanComparisonNodeState.Removed => $"{node.PhysicalOp} [Removed]",
                Core.Services.PlanComparisonNodeState.OperatorChanged =>
                    $"{node.PhysicalOp} [from {node.OtherPhysicalOp}]",
                _ => node.PhysicalOp
            };
        }

        private static void ApplyComparisonStateStyle(
            Core.Services.PlanComparisonNodeState state,
            bool isPlanB,
            Border border,
            TextBlock textBlockOp)
        {
            switch (state)
            {
                case Core.Services.PlanComparisonNodeState.Added:
                case Core.Services.PlanComparisonNodeState.Removed:
                    border.Background = isPlanB
                        ? new SolidColorBrush(Color.FromArgb(40, 76, 175, 80))
                        : new SolidColorBrush(Color.FromArgb(40, 244, 67, 54));
                    border.BorderBrush = isPlanB ? Brushes.Green : Brushes.Red;
                    border.BorderThickness = new Thickness(1);
                    textBlockOp.Foreground = isPlanB ? Brushes.DarkGreen : Brushes.DarkRed;
                    break;
                case Core.Services.PlanComparisonNodeState.OperatorChanged:
                    border.Background = new SolidColorBrush(Color.FromArgb(40, 255, 152, 0));
                    border.BorderBrush = Brushes.Orange;
                    border.BorderThickness = new Thickness(1);
                    textBlockOp.Foreground = Brushes.DarkOrange;
                    break;
            }
        }

        private static void ApplyCostDeltaStyle(
            Core.Services.PlanComparisonNode node,
            TextBlock textBlockCost)
        {
            if (node.State != Core.Services.PlanComparisonNodeState.Unchanged ||
                Math.Abs(node.CostPercentDelta) <= 5)
            {
                return;
            }

            if (node.CostPercentDelta > 0)
            {
                textBlockCost.Foreground = Brushes.Red;
                textBlockCost.Text += $" (+{node.CostPercentDelta:F1}%)";
            }
            else
            {
                textBlockCost.Foreground = Brushes.Green;
                textBlockCost.Text += $" ({node.CostPercentDelta:F1}%)";
            }
        }

        private static string FormatRuntimeDelta(Core.Services.RuntimeMetricDelta delta)
        {
            if (Math.Abs(delta.Delta) < 1e-9)
            {
                return $"{delta.Label}: {delta.Value}";
            }

            string sign = delta.Delta > 0 ? "+" : "-";
            return $"{delta.Label}: {delta.Value} ({sign}{Math.Abs(delta.Delta)})";
        }

        #endregion

        #region 其他功能

        private void ExportObfuscatedPlan_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentPlanDoc == null)
            {
                MessageBox.Show("Please load an execution plan first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? fileName = ShowSaveFileDialog(
                "Execution plan files (*.sqlplan)|*.sqlplan|XML files (*.xml)|*.xml",
                "Export obfuscated execution plan",
                ".sqlplan",
                "Obfuscated_Plan.sqlplan");

            if (fileName == null)
            {
                return;
            }

            try
            {
                StatusTextBlock.Text = "Generating obfuscated plan...";
                var maskedDoc = SqlXmlAnalyzer.Core.Services.PlanObfuscatorService.ObfuscatePlan(ViewModel.CurrentPlanDoc);
                maskedDoc.Save(fileName);
                MessageBox.Show($"Obfuscated execution plan saved to:\n{fileName}\n\nSensitive table names and SQL text have been replaced, and the file remains readable by SSMS.", "Export succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusTextBlock.Text = "Ready";
            }
            catch (Exception ex)
            {
                Logger.LogException("ExportObfuscatedPlan_Click", ex);
                MessageBox.Show($"Export failed:\n{ex.Message}", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Obfuscated export failed";
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

                    var parseResult = DeadlockXmlParser.TryParseDeadlockXml(ViewModel.CurrentDeadlockDoc);
                    if (!parseResult.IsSuccess || parseResult.Value == null)
                    {
                        throw new InvalidDataException(string.Join(Environment.NewLine, parseResult.Errors));
                    }
                    var parsed = parseResult.Value;
                    var graph = DeadlockGraphBuilder.Build(parsed.Processes, parsed.Resources, parsed.VictimId);
                    string mermaid = DeadlockGraphBuilder.GenerateMermaid(graph, true);

                    string summaryText = $"死锁文件: {Path.GetFileName(ViewModel.CurrentDeadlockFilePath)}\n受害者进程: {parsed.VictimId}\n参与 SPID: {string.Join(", ", parsed.Processes.Select(p => p.Spid).Distinct())}";

                    var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph, ViewModel.CurrentDeadlockDoc);
                    var reportItems = patterns
                        .Select(p => new HtmlReportItem(
                            p.TypeName,
                            p.Description,
                            p.LikelyCause,
                            p.Recommendation,
                            p.Severity))
                        .ToList();

                    if (!string.IsNullOrWhiteSpace(ViewModel.DeadlockPatternText))
                    {
                        reportItems.Add(new HtmlReportItem(
                            "分析与选中项详情",
                            ViewModel.DeadlockPatternText,
                            string.Empty,
                            string.Empty,
                            "Info"));
                    }

                    var reportSections = new[]
                    {
                        new HtmlReportSection("💡 详细诊断与建议", reportItems)
                    };

                    string? reportPath = ShowSaveFileDialog(
                        "HTML report (*.html)|*.html",
                        "Save deadlock analysis report",
                        ".html",
                        $"DeadlockReport_{Path.GetFileNameWithoutExtension(ViewModel.CurrentDeadlockFilePath)}.html");

                    if (reportPath != null)
                    {
                        HtmlReportGenerator.SaveReport(ViewModel.CurrentDeadlockFilePath, "Deadlock", summaryText, mermaid, reportSections, reportPath);
                        Logger.Info($"Deadlock HTML report saved to {reportPath}");

                        if (MessageBox.Show("Report saved successfully. Open it now?", "Save succeeded", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            _browserLauncher.OpenFile(reportPath);
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
                    var planResults = PlanDiagnosticAnalyzer.AnalyzePlan(ViewModel.CurrentPlanDoc, _showplanNs);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ViewModel.MissingIndexes.Clear();
                        var mis = PlanDiagnosticAnalyzer.ExtractMissingIndexes(ViewModel.CurrentPlanDoc, _showplanNs);
                        foreach (var m in mis)
                        {
                            ViewModel.MissingIndexes.Add(m);
                        }
                    });

                    var reportItems = planResults
                        .Select(result => new HtmlReportItem(
                            result.Title,
                            result.Message,
                            string.Empty,
                            string.Empty,
                            result.Severity))
                        .ToList();

                    if (reportItems.Count == 0)
                    {
                        reportItems.Add(new HtmlReportItem(
                            "未发现规则告警",
                            "当前执行计划未命中已启用的诊断规则。",
                            string.Empty,
                            string.Empty,
                            "Info"));
                    }

                    var reportSections = new[]
                    {
                        new HtmlReportSection("💡 详细诊断与建议", reportItems)
                    };

                    string summaryText = $"执行计划文件: {Path.GetFileName(ViewModel.CurrentPlanFilePath)}\n";
                    var queryPlans = ViewModel.CurrentPlanDoc.Descendants(_showplanNs + "QueryPlan").ToList();
                    if (queryPlans.Count > 0)
                    {
                        summaryText += $"估算总成本: {queryPlans[0].Attribute("EstimatedTotalSubtreeCost")?.Value ?? "N/A"}\n";
                    }

                    string? reportPath = ShowSaveFileDialog(
                        "HTML report (*.html)|*.html",
                        "Save execution plan analysis report",
                        ".html",
                        $"ExecutionPlanReport_{Path.GetFileNameWithoutExtension(ViewModel.CurrentPlanFilePath)}.html");

                    if (reportPath != null)
                    {
                        HtmlReportGenerator.SaveReport(ViewModel.CurrentPlanFilePath, "ExecutionPlan", summaryText, mermaid, reportSections, reportPath);
                        Logger.Info($"Execution plan HTML report saved to {reportPath}");

                        if (MessageBox.Show("Report saved successfully. Open it now?", "Save succeeded", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            _browserLauncher.OpenFile(reportPath);
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

        private void ExportReport(string extension, string filter)
        {
            try
            {
                string title;
                string content;
                string defaultFileName;
                FrameworkElement? imageElement = null;

                if (MainTabControl.SelectedIndex == 0)
                {
                    if (ViewModel.CurrentDeadlockDoc == null || ViewModel.CurrentDeadlockFilePath == null)
                    {
                        MessageBox.Show("There is no loaded deadlock document to export.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    title = "SQL Server Deadlock Diagnostic Report";
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("=== Deadlock Pattern Diagnostics ===");
                    var patterns = DeadlockPatternsListBox.ItemsSource as System.Collections.IEnumerable;
                    bool hasPatterns = false;
                    if (patterns != null)
                    {
                        foreach (var item in patterns)
                        {
                            if (item is DeadlockPattern pattern)
                            {
                                sb.AppendLine(pattern.TypeName);
                                sb.AppendLine($"Description: {pattern.Description}");
                                sb.AppendLine($"Likely cause: {pattern.LikelyCause}");
                                sb.AppendLine($"Recommendation: {pattern.Recommendation}");
                                sb.AppendLine();
                                hasPatterns = true;
                            }
                        }
                    }

                    if (!hasPatterns)
                    {
                        sb.AppendLine("No known deadlock pattern was detected.");
                        sb.AppendLine();
                    }

                    if (!string.IsNullOrWhiteSpace(ViewModel.DeadlockPatternText))
                    {
                        sb.AppendLine("=== Selected Item Analysis ===");
                        content = ViewModel.DeadlockPatternText
                            .Replace("馃拃", "")
                            .Replace("馃攳", "")
                            .Replace("馃挕", "")
                            .Replace("馃敶", "")
                            .Replace("馃煝", "")
                            .Replace("馃煚", "")
                            .Replace("馃搵", "");
                        sb.AppendLine(content);
                    }

                    content = sb.ToString();
                    defaultFileName = $"DeadlockReport_{Path.GetFileNameWithoutExtension(ViewModel.CurrentDeadlockFilePath)}.{extension}";
                    imageElement = DeadlockCanvasBorder;
                }
                else if (MainTabControl.SelectedIndex == 1)
                {
                    if (string.IsNullOrWhiteSpace(ViewModel.PlanWarningsText) || ViewModel.CurrentPlanFilePath == null)
                    {
                        MessageBox.Show("There are no execution plan diagnostics to export.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    title = "SQL Server Execution Plan Diagnostic Report";
                    content = ViewModel.PlanWarningsText;
                    defaultFileName = $"PlanReport_{Path.GetFileNameWithoutExtension(ViewModel.CurrentPlanFilePath)}.{extension}";
                }
                else
                {
                    return;
                }

                string? fileName = ShowSaveFileDialog(
                    filter,
                    $"Save {extension.ToUpperInvariant()} analysis report",
                    $".{extension}",
                    defaultFileName);

                if (fileName != null)
                {
                    _pdfWordReportService.Export(
                        extension,
                        fileName,
                        title,
                        content,
                        imageElement);
                    MessageBox.Show($"{extension.ToUpperInvariant()} report exported successfully.", "Export succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException($"ExportTo{extension.ToUpperInvariant()}_Click", ex);
                MessageBox.Show($"Export failed:\n{ex.Message}", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void CopyRefactoredSql_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentRefactoredSql))
            {
                Clipboard.SetText(_currentRefactoredSql);
                MessageBox.Show("重构后的 SQL 已成功复制到剪贴板！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CompareSql_Click(object sender, RoutedEventArgs e)
        {
            if (OriginalSqlCol.Width.Value == 0)
            {
                OriginalSqlCol.Width = new GridLength(1, GridUnitType.Star);
                SqlSplitterCol.Width = new GridLength(4);
                SqlGridSplitter.Visibility = Visibility.Visible;
                BtnCompareSql.Content = "隐藏原始 SQL";
            }
            else
            {
                OriginalSqlCol.Width = new GridLength(0);
                SqlSplitterCol.Width = new GridLength(0);
                SqlGridSplitter.Visibility = Visibility.Collapsed;
                BtnCompareSql.Content = "显示原始 SQL";
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
                    _browserLauncher.OpenFolder(logsPath);
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
            var result = MessageBox.Show("SqlXmlAnalyzer 专业图形界面版 v2.0\n\n" +
                                         "功能特性：\n" +
                                         "1. 完美的执行计划可视化与智能折叠 (基于 Nodify)\n" +
                                         "2. 深度死锁回放与有向图关键路径聚焦\n" +
                                         "3. 索引调优沙盒与 Tipping Point 临界线分析\n" +
                                         "4. 参数嗅探并排对比与直方图绘制\n\n" +
                                         "是否关联 .sqlplan 与 .xdl 文件到系统右键菜单？\n" +
                                         "（点击“是”将为当前用户注册文件关联，“否”则仅关闭此窗口）",
                                         "关于 & 关联设置", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    RegisterFileAssociations();
                    MessageBox.Show("文件关联注册成功！您现在可以直接双击或右键打开 .sqlplan 和 .xdl 文件了。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"注册文件关联失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public static void RegisterFileAssociations()
        {
            string appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(appPath)) return;

            using (var classesKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes", true))
            {
                if (classesKey == null) return;

                // 1. .sqlplan 关联
                using (var sqlplanKey = classesKey.CreateSubKey(".sqlplan"))
                {
                    sqlplanKey.SetValue("", "SqlXmlAnalyzer.sqlplan");
                }
                using (var sqlplanProgKey = classesKey.CreateSubKey("SqlXmlAnalyzer.sqlplan"))
                {
                    sqlplanProgKey.SetValue("", "SQL Server 执行计划文件 (.sqlplan)");
                    sqlplanProgKey.SetValue("FriendlyTypeName", "SQL Server 执行计划文件 (.sqlplan)");
                    using (var shellKey = sqlplanProgKey.CreateSubKey(@"shell\open\command"))
                    {
                        shellKey.SetValue("", $"\"{appPath}\" \"%1\"");
                    }
                }

                // 2. .xdl 关联
                using (var xdlKey = classesKey.CreateSubKey(".xdl"))
                {
                    xdlKey.SetValue("", "SqlXmlAnalyzer.xdl");
                }
                using (var xdlProgKey = classesKey.CreateSubKey("SqlXmlAnalyzer.xdl"))
                {
                    xdlProgKey.SetValue("", "SQL Server 死锁文件 (.xdl)");
                    xdlProgKey.SetValue("FriendlyTypeName", "SQL Server 死锁文件 (.xdl)");
                    using (var shellKey = xdlProgKey.CreateSubKey(@"shell\open\command"))
                    {
                        shellKey.SetValue("", $"\"{appPath}\" \"%1\"");
                    }
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
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

                var parseResult = DeadlockXmlParser.TryParseDeadlockXml(ViewModel.CurrentDeadlockDoc);
                if (!parseResult.IsSuccess || parseResult.Value == null)
                {
                    throw new InvalidDataException(string.Join(Environment.NewLine, parseResult.Errors));
                }
                var parsed = parseResult.Value;
                var graph = DeadlockGraphBuilder.Build(parsed.Processes, parsed.Resources, parsed.VictimId);
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
                    foreach (var col in child.Descendants(child.Name.Namespace + "ColumnReference"))
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
            public string Group { get; set; } = "";
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
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

                var parseResult = DeadlockXmlParser.TryParseDeadlockXml(ViewModel.CurrentDeadlockDoc);
                if (!parseResult.IsSuccess || parseResult.Value == null)
                {
                    throw new InvalidDataException(string.Join(Environment.NewLine, parseResult.Errors));
                }
                var parsed = parseResult.Value;
                var graph = DeadlockGraphBuilder.Build(parsed.Processes, parsed.Resources, parsed.VictimId);
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
            _browserLauncher.OpenMermaid(mermaidCode);
        }

        #endregion

        private void PlanNodifyGraph_Loaded(object sender, RoutedEventArgs e)
        {

        }

        // --- 调优历史与 A/B 并排对比事件处理器 ---
        private async void TuningHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TuningHistoryListView.SelectedItem is Core.ViewModels.PlanSnapshot snapshot)
            {
                var session = _analysisSessions.Begin();
                ViewModel.CurrentPlanFilePath = snapshot.FilePath;
                await AnalyzeExecutionPlanDocumentAsync(
                    snapshot.Document,
                    snapshot.FilePath,
                    session.RequestId,
                    session.Token);
            }
        }

        private void SaveSession_Click(object sender, RoutedEventArgs e)
        {
            string? fileName = ShowSaveFileDialog(
                "SqlXmlAnalyzer tuning session (*.pesession)|*.pesession",
                "Save current tuning session",
                ".pesession",
                "Tuning_Session.pesession");

            if (fileName != null)
            {
                ViewModel.SaveSession(fileName);
            }
        }

        private void LoadSession_Click(object sender, RoutedEventArgs e)
        {
            string? fileName = ShowOpenFileDialog(
                "SqlXmlAnalyzer tuning session (*.pesession)|*.pesession",
                "Open tuning session",
                ".pesession");

            if (fileName != null)
            {
                ViewModel.LoadSession(fileName);
            }
        }

        private void SwapPlanAB_Click(object sender, RoutedEventArgs e)
        {
            var temp = ViewModel.PlanA;
            ViewModel.PlanA = ViewModel.PlanB;
            ViewModel.PlanB = temp;
        }

        private void StatisticsHistogramView_Loaded(object sender, RoutedEventArgs e)
        {

        }

        #region 可视化看板与交互展示 (GUI Dashboard Integration & Interactive Visualization)

        private static readonly Brush AdditionBrush = CreateFrozenBrush(Color.FromRgb(232, 245, 233)); // #E8F5E9
        private static readonly Brush DeletionBrush = CreateFrozenBrush(Color.FromRgb(255, 235, 238)); // #FFEBEE
        private static readonly Brush ModificationBrush = CreateFrozenBrush(Color.FromRgb(227, 242, 253)); // #E3F2FD
        private static readonly Brush PlaceholderBrush = CreateFrozenBrush(Color.FromRgb(245, 245, 245)); // #F5F5F5

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static readonly HashSet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "ON", "GROUP", "BY", "ORDER",
            "HAVING", "AND", "OR", "NOT", "IN", "EXISTS", "LIKE", "AS", "CREATE", "INDEX", "DROP", "TABLE",
            "INSERT", "UPDATE", "DELETE", "INTO", "VALUES", "SET", "EXEC", "PROCEDURE", "DECLARE", "WITH",
            "UNION", "ALL", "CASE", "WHEN", "THEN", "ELSE", "END", "NULL", "IS", "CAST", "CONVERT", "GO",
            "CROSS", "APPLY", "TOP", "DISTINCT"
        };

        private static readonly System.Text.RegularExpressions.Regex SqlTokenizerRegex =
            new System.Text.RegularExpressions.Regex(
                @"(--.*)|('[^']*(?:''[^']*)*')|([a-zA-Z_#@][a-zA-Z0-9_]*)|(\s+)|(.)",
                System.Text.RegularExpressions.RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(100));

        private T? FindVisualChild<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        return (T)child;
                    }

                    T? childItem = FindVisualChild<T>(child);
                    if (childItem != null)
                    {
                        return childItem;
                    }
                }
            }
            return null;
        }

        private void SetupSynchronizedScrolling()
        {
            _originalScroll = FindVisualChild<ScrollViewer>(OriginalSqlTextBox);
            _refactoredScroll = FindVisualChild<ScrollViewer>(RefactoredSqlTextBox);

            if (_originalScroll != null)
            {
                _originalScroll.ScrollChanged += OriginalScroll_ScrollChanged;
            }
            if (_refactoredScroll != null)
            {
                _refactoredScroll.ScrollChanged += RefactoredScroll_ScrollChanged;
            }
        }

        private void OriginalScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSynchronizingScroll) return;
            if (_refactoredScroll == null || _originalScroll == null) return;

            _isSynchronizingScroll = true;
            try
            {
                _refactoredScroll.ScrollToVerticalOffset(e.VerticalOffset);
                _refactoredScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            }
            finally
            {
                _isSynchronizingScroll = false;
            }
        }

        private void RefactoredScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSynchronizingScroll) return;
            if (_originalScroll == null || _refactoredScroll == null) return;

            _isSynchronizingScroll = true;
            try
            {
                _originalScroll.ScrollToVerticalOffset(e.VerticalOffset);
                _originalScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            }
            finally
            {
                _isSynchronizingScroll = false;
            }
        }

        private void UpdateSqlDiffViews()
        {
            if (string.IsNullOrEmpty(_currentOriginalSql) && string.IsNullOrEmpty(_currentRefactoredSql))
            {
                OriginalSqlTextBox.Document.Blocks.Clear();
                RefactoredSqlTextBox.Document.Blocks.Clear();
                return;
            }

            string[] linesOriginal = _currentOriginalSql.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string[] linesRefactored = _currentRefactoredSql.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var (alignedOriginal, alignedRefactored) = AlignLines(linesOriginal, linesRefactored);

            RenderAlignedDiff(OriginalSqlTextBox, alignedOriginal, false, alignedRefactored);
            RenderAlignedDiff(RefactoredSqlTextBox, alignedRefactored, true, alignedOriginal);
        }

        private (List<string?> alignedOriginal, List<string?> alignedRefactored) AlignLines(string[] linesA, string[] linesB)
        {
            int N = linesA.Length;
            int M = linesB.Length;

            if (N > 1000 || M > 1000)
            {
                // Fallback: simple line-by-line alignment without DP to avoid OOM or UI freezing for massive queries
                List<string?> fallbackA = new List<string?>();
                List<string?> fallbackB = new List<string?>();
                int minLen = Math.Min(N, M);
                for (int i = 0; i < minLen; i++)
                {
                    fallbackA.Add(linesA[i]);
                    fallbackB.Add(linesB[i]);
                }
                if (N > M)
                {
                    for (int i = minLen; i < N; i++)
                    {
                        fallbackA.Add(linesA[i]);
                        fallbackB.Add(null);
                    }
                }
                else if (M > N)
                {
                    for (int i = minLen; i < M; i++)
                    {
                        fallbackA.Add(null);
                        fallbackB.Add(linesB[i]);
                    }
                }
                return (fallbackA, fallbackB);
            }

            int[,] dp = new int[N + 1, M + 1];

            for (int i = 1; i <= N; i++)
            {
                for (int j = 1; j <= M; j++)
                {
                    if (NormalizeForDiff(linesA[i - 1]) == NormalizeForDiff(linesB[j - 1]))
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                    }
                }
            }

            List<string?> alignedA = new List<string?>();
            List<string?> alignedB = new List<string?>();
            int currI = N;
            int currJ = M;

            while (currI > 0 || currJ > 0)
            {
                if (currI > 0 && currJ > 0 && NormalizeForDiff(linesA[currI - 1]) == NormalizeForDiff(linesB[currJ - 1]))
                {
                    alignedA.Add(linesA[currI - 1]);
                    alignedB.Add(linesB[currJ - 1]);
                    currI--;
                    currJ--;
                }
                else if (currJ > 0 && (currI == 0 || dp[currI, currJ - 1] >= dp[currI - 1, currJ]))
                {
                    alignedA.Add(null);
                    alignedB.Add(linesB[currJ - 1]);
                    currJ--;
                }
                else
                {
                    alignedA.Add(linesA[currI - 1]);
                    alignedB.Add(null);
                    currI--;
                }
            }

            alignedA.Reverse();
            alignedB.Reverse();

            // Post-process single line diffs to pair them up side-by-side
            for (int i = 0; i < alignedA.Count - 1; i++)
            {
                if (alignedA[i] != null && alignedB[i] == null && alignedA[i + 1] == null && alignedB[i + 1] != null)
                {
                    alignedB[i] = alignedB[i + 1];
                    alignedA.RemoveAt(i + 1);
                    alignedB.RemoveAt(i + 1);
                }
                else if (alignedA[i] == null && alignedB[i] != null && alignedA[i + 1] != null && alignedB[i + 1] == null)
                {
                    alignedA[i] = alignedA[i + 1];
                    alignedA.RemoveAt(i + 1);
                    alignedB.RemoveAt(i + 1);
                }
            }

            return (alignedA, alignedB);
        }

        private string NormalizeForDiff(string s)
        {
            if (s == null) return "";
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "").ToLowerInvariant();
        }

        private void RenderAlignedDiff(RichTextBox rtb, List<string?> lines, bool isRefactoredSide, List<string?> opposingLines)
        {
            rtb.Document.Blocks.Clear();
            rtb.BeginChange();
            try
            {
                List<Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery>? subqueries = null;
                List<int>? lineStartOffsets = null;
                HashSet<Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery>? handledSubqueries = null;

                if (!isRefactoredSide && !string.IsNullOrEmpty(_currentOriginalSql))
                {
                    subqueries = SqlXmlAnalyzer.Refactoring.Rules.ScalarSubqueryToJoinRule.GetRewriteableSubqueries(_currentOriginalSql);
                    lineStartOffsets = GetLineStartOffsets(_currentOriginalSql);
                    handledSubqueries = new HashSet<Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery>();
                }

                int realLineIdx = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    string? line = lines[i];
                    string? opposingLine = opposingLines[i];

                    Paragraph p = new Paragraph();
                    p.Margin = new Thickness(0, 1, 0, 1);

                    Brush defaultForeground = Brushes.Black;

                    if (line == null)
                    {
                        p.Background = PlaceholderBrush;
                        p.Inlines.Add(new Run(" ") { Foreground = Brushes.Transparent });
                    }
                    else
                    {
                        int lineStartOffset = 0;
                        if (!isRefactoredSide && lineStartOffsets != null && realLineIdx < lineStartOffsets.Count)
                        {
                            lineStartOffset = lineStartOffsets[realLineIdx];
                        }

                        if (opposingLine == null)
                        {
                            p.Background = isRefactoredSide ? AdditionBrush : DeletionBrush;
                            FormatSqlLine(p, line, defaultForeground, subqueries, lineStartOffset, handledSubqueries);
                        }
                        else if (NormalizeForDiff(line) != NormalizeForDiff(opposingLine))
                        {
                            p.Background = ModificationBrush;
                            FormatSqlLine(p, line, defaultForeground, subqueries, lineStartOffset, handledSubqueries);
                        }
                        else
                        {
                            FormatSqlLine(p, line, defaultForeground, subqueries, lineStartOffset, handledSubqueries);
                        }

                        if (!isRefactoredSide)
                        {
                            realLineIdx++;
                        }
                    }

                    rtb.Document.Blocks.Add(p);
                }
            }
            finally
            {
                rtb.EndChange();
            }
        }

        private void FormatSqlLine(Paragraph p, string text, Brush defaultForeground, List<Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery>? subqueries = null, int lineStartOffset = 0, HashSet<Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery>? handledSubqueries = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                p.Inlines.Add(new Run(""));
                return;
            }

            try
            {
                var matches = SqlTokenizerRegex.Matches(text);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    int tokenAbsoluteStart = lineStartOffset + match.Index;
                    int tokenAbsoluteEnd = tokenAbsoluteStart + match.Length;

                    Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery? overlappingSubquery = null;
                    if (subqueries != null)
                    {
                        foreach (var sub in subqueries)
                        {
                            int subStart = sub.StartOffset;
                            int subEnd = subStart + sub.FragmentLength;
                            if (tokenAbsoluteStart < subEnd && tokenAbsoluteEnd > subStart)
                            {
                                overlappingSubquery = sub;
                                break;
                            }
                        }
                    }

                    if (overlappingSubquery != null && handledSubqueries != null && !handledSubqueries.Contains(overlappingSubquery))
                    {
                        handledSubqueries.Add(overlappingSubquery);
                        var lightbulbBtn = CreateLightbulbButton(overlappingSubquery);
                        p.Inlines.Add(new InlineUIContainer(lightbulbBtn) { BaselineAlignment = BaselineAlignment.Center });
                    }

                    Run run;
                    if (match.Groups[1].Success) // Comment
                    {
                        run = new Run(match.Value) { Foreground = Brushes.Green };
                    }
                    else if (match.Groups[2].Success) // String literal
                    {
                        run = new Run(match.Value) { Foreground = Brushes.Brown };
                    }
                    else if (match.Groups[3].Success) // Word / Identifier
                    {
                        string val = match.Value;
                        if (SqlKeywords.Contains(val))
                        {
                            run = new Run(val) { Foreground = Brushes.Blue, FontWeight = FontWeights.Bold };
                        }
                        else
                        {
                            run = new Run(val) { Foreground = defaultForeground };
                        }
                    }
                    else // Whitespace, operators, or anything else
                    {
                        run = new Run(match.Value) { Foreground = defaultForeground };
                    }

                    if (overlappingSubquery != null)
                    {
                        run.TextDecorations = SquigglyUnderline;
                        run.ToolTip = "标量子查询可优化为 JOIN，点击灯泡一键修复并对比效果";
                    }

                    p.Inlines.Add(run);
                }
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                // Fallback to plain text on timeout to prevent freezing
                p.Inlines.Add(new Run(text) { Foreground = defaultForeground });
            }
        }

        private List<int> GetLineStartOffsets(string sql)
        {
            var list = new List<int>();
            if (string.IsNullOrEmpty(sql)) return list;

            list.Add(0);
            for (int i = 0; i < sql.Length; i++)
            {
                if (sql[i] == '\r')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\n')
                    {
                        i++;
                    }
                    list.Add(i + 1);
                }
                else if (sql[i] == '\n')
                {
                    list.Add(i + 1);
                }
            }
            return list;
        }

        private UIElement CreateLightbulbButton(Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery subquery)
        {
            var textBlock = new TextBlock
            {
                Text = "💡",
                ToolTip = "标量子查询可优化为 JOIN，点击一键修复并对比效果",
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            textBlock.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    e.Handled = true;
                    QuickFix_Click(subquery);
                }
            };
            return textBlock;
        }

        private void QuickFix_Click(Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery subquery)
        {
            if (!SqlXmlAnalyzer.Refactoring.Rules.ScalarSubqueryToJoinRule.TryRewriteSelectedSubquery(
                    _currentOriginalSql,
                    subquery.StartOffset,
                    subquery.FragmentLength,
                    out var selectedRewriteSql))
            {
                MessageBox.Show("无法安全地重写所选标量子查询。SQL 未被修改。", "快速修复不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new QuickFixWindow(_currentOriginalSql, selectedRewriteSql, subquery)
            {
                Owner = this
            };
            dialog.ShowDialog();
            if (dialog.Applied)
            {
                _currentOriginalSql = selectedRewriteSql;
                _currentRefactoredSql = selectedRewriteSql;
                UpdateSqlDiffViews();
                PlanStatementTextBox.Text = _currentOriginalSql.Length > 800 ? _currentOriginalSql.Substring(0, 800) + "..." : _currentOriginalSql;
                MessageBox.Show("已仅应用所选标量子查询的 JOIN 重写。", "修复成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyIndexDdl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string ddl && !string.IsNullOrEmpty(ddl))
            {
                Clipboard.SetText(ddl);
                MessageBox.Show("CREATE INDEX DDL 已成功复制到剪贴板！", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyRollbackDdl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string ddl && !string.IsNullOrEmpty(ddl))
            {
                Clipboard.SetText(ddl);
                MessageBox.Show("DROP INDEX (回滚) DDL 已成功复制到剪贴板！", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyDeploymentBundle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion mi)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("/*******************************************************************************");
                sb.AppendLine($" * SQL Server Missing Index Deployment Bundle");
                sb.AppendLine($" * Table:  {mi.Table}");
                if (!string.IsNullOrEmpty(mi.Schema))
                {
                    sb.AppendLine($" * Schema: {mi.Schema}");
                }
                sb.AppendLine($" * Impact: {mi.Impact:F2}%");
                sb.AppendLine($" * Score:  {mi.Score}/100");
                sb.AppendLine(" *******************************************************************************/");
                sb.AppendLine();
                sb.AppendLine("-- === 1. DEPLOYMENT DDL (CREATE INDEX) ===");
                sb.AppendLine("BEGIN TRANSACTION;");
                sb.AppendLine("BEGIN TRY");
                sb.AppendLine("    " + mi.CreateIndexStatement);
                sb.AppendLine("    COMMIT TRANSACTION;");
                sb.AppendLine("    PRINT 'Missing Index deployed successfully.';");
                sb.AppendLine("END TRY");
                sb.AppendLine("BEGIN CATCH");
                sb.AppendLine("    ROLLBACK TRANSACTION;");
                sb.AppendLine("    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();");
                sb.AppendLine("    RAISERROR(@ErrMsg, 16, 1);");
                sb.AppendLine("END CATCH");
                sb.AppendLine();
                sb.AppendLine("-- === 2. ROLLBACK DDL (DROP INDEX) ===");
                sb.AppendLine("/*");
                sb.AppendLine("    " + mi.RollbackStatement);
                sb.AppendLine("*/");

                Clipboard.SetText(sb.ToString());
                MessageBox.Show("完整部署包 (包含安全事务与回滚脚本) 已复制到剪贴板！", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion
    }
}
