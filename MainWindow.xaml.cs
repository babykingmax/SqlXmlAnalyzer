using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Services;
using MessageBox = System.Windows.MessageBox;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow : Window
    {
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChromeInterop.Attach(this);
        }

        public Core.ViewModels.MainViewModel ViewModel { get; }
        private readonly TemporaryFileManager _temporaryFileManager;
        private readonly Core.Services.AnalysisSessionCoordinator _analysisSessions;
        private readonly Core.Services.BrowserLauncher _browserLauncher;
        private readonly MainWindowShellActionService _shellActionService;
        private readonly Core.Services.DocumentOpenService _documentOpenService;
        private readonly Core.Services.DeadlockDocumentController _deadlockDocumentController;
        private readonly Core.Services.PlanDocumentController _planDocumentController;
        private readonly Core.Services.DocumentRefreshActionService _documentRefreshActionService;
        private readonly Core.Services.PlanComparisonController _planComparisonController;
        private readonly Core.Services.PlanComparisonTreeService _planComparisonTreeService;
        private readonly Core.Services.PlanComparisonTreeViewRenderer _planComparisonTreeViewRenderer;
        private readonly Core.Services.MermaidDiagramService _mermaidDiagramService;
        private readonly MermaidDiagramUiActionService _mermaidDiagramUiActionService;
        private readonly Core.Services.AnalysisReportController _analysisReportController;
        private readonly ReportExportUiActionService _reportExportUiActionService;
        private readonly PlanObfuscationExportUiActionService _planObfuscationExportUiActionService;
        private readonly FileOpenUiActionService _fileOpenUiActionService;
        private readonly XelDeadlockUiActionService _xelDeadlockUiActionService;
        private readonly Core.Services.IFileDialogService _fileDialogService;
        private readonly MissingIndexClipboardUiActionService _missingIndexClipboardUiActionService;
        private readonly Core.Services.DeadlockSelectionDetailService _deadlockSelectionDetailService;
        private readonly Core.Services.DeadlockGraphViewportService _deadlockGraphViewportService;
        private readonly Core.Services.DeadlockGraphGeometryService _deadlockGraphGeometryService;
        private readonly DeadlockGraphEdgeElementFactory _deadlockGraphEdgeElementFactory;
        private readonly DeadlockCanvasInteractionBinder _deadlockCanvasInteractionBinder;
        private readonly DeadlockNodeInteractionBinder _deadlockNodeInteractionBinder;
        private readonly Core.Services.DeadlockGraphLayoutService _deadlockGraphLayoutService;
        private readonly Core.Services.DeadlockGraphEdgeService _deadlockGraphEdgeService;
        private readonly Core.Services.DeadlockGraphEdgeRegistryService _deadlockGraphEdgeRegistryService;
        private readonly Core.Services.DeadlockGraphPlacementService _deadlockGraphPlacementService;
        private readonly DeadlockGraphNodeElementFactory _deadlockGraphNodeElementFactory;
        private readonly Core.Services.DeadlockPlaybackStateService _deadlockPlaybackStateService;
        private readonly Core.Services.DeadlockGraphVisualStateService _deadlockGraphVisualStateService;
        private readonly DeadlockGraphPlaybackVisualService _deadlockGraphPlaybackVisualService;
        private readonly Core.Services.DeadlockStepBadgeService _deadlockStepBadgeService;
        private readonly Core.Services.WorkspacePanelLayoutService _workspacePanelLayoutService;
        private readonly TuningSessionUiActionService _tuningSessionUiActionService;
        private readonly Core.Services.PlanTreeService _planTreeService;
        private readonly PlanSelectionUiActionService _planSelectionUiActionService;
        private readonly Core.Services.PlanOperatorTreeViewRenderer _planOperatorTreeViewRenderer;
        private readonly SqlDiffScrollSyncService _sqlDiffScrollSyncService;
        private readonly SqlDiffUiActionService _sqlDiffUiActionService;
        private readonly SqlQuickFixUiActionService _sqlQuickFixUiActionService;

        private Dictionary<string, FrameworkElement> _nodeElements = new Dictionary<string, FrameworkElement>();
        private Dictionary<(string, string), DeadlockGraphEdgeElements> _arrowCache = new Dictionary<(string, string), DeadlockGraphEdgeElements>();
        private List<Core.Services.DeadlockGraphEdge> _edgesForDrawing = new();

        private DeadlockTimelineParser.ParsedDeadlock? _currentTimeline;
        private DeadlockPlaybackViewModel? _playbackViewModel;
        private Dictionary<(string, string), Border> _stepBadges = new Dictionary<(string, string), Border>();

        private string _currentOriginalSql = "";
        private string _currentRefactoredSql = "";

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
            Core.Services.LogFolderActionService? logFolderActionService = null,
            Core.Services.IPdfWordReportExporter? pdfWordReportService = null,
            Core.Services.DocumentOpenService? documentOpenService = null,
            Core.Services.DeadlockDocumentController? deadlockDocumentController = null,
            Core.Services.PlanDocumentController? planDocumentController = null,
            Core.Services.DocumentRefreshActionService? documentRefreshActionService = null,
            Core.Services.PlanComparisonController? planComparisonController = null,
            Core.Services.PlanComparisonTreeService? planComparisonTreeService = null,
            Core.Services.PlanComparisonTreeViewRenderer? planComparisonTreeViewRenderer = null,
            Core.Services.MermaidDiagramService? mermaidDiagramService = null,
            Core.Services.MermaidDiagramActionService? mermaidDiagramActionService = null,
            Core.Services.AnalysisReportController? analysisReportController = null,
            Core.Services.HtmlReportActionService? htmlReportActionService = null,
            Core.Services.PortableReportActionService? portableReportActionService = null,
            Core.Services.HtmlReportExportService? htmlReportExportService = null,
            Core.Services.PortableReportExportService? portableReportExportService = null,
            Core.Services.TuningSessionService? tuningSessionService = null,
            Core.Services.PlanPropertyService? planPropertyService = null,
            Core.Services.PlanTreeService? planTreeService = null,
            Core.Services.PlanSelectionActionService? planSelectionActionService = null,
            Core.Services.SqlDiffService? sqlDiffService = null,
            Core.Services.SqlDiffDocumentRenderer? sqlDiffDocumentRenderer = null,
            Core.Services.SqlQuickFixService? sqlQuickFixService = null,
            Core.Services.IFileDialogService? fileDialogService = null,
            Core.Services.FileAssociationRegistrationService? fileAssociationRegistrationService = null,
            Core.Services.AnalysisClipboardService? analysisClipboardService = null,
            Core.Services.MissingIndexDeploymentScriptService? missingIndexDeploymentScriptService = null,
            Core.Services.MissingIndexClipboardActionService? missingIndexClipboardActionService = null,
            Core.Services.DeadlockSelectionDetailService? deadlockSelectionDetailService = null,
            DeadlockAnalysisService? deadlockAnalysisService = null,
            Core.Services.PlanAnalysisService? planAnalysisService = null,
            Core.Services.PlanOperatorTreeViewRenderer? planOperatorTreeViewRenderer = null,
            Core.Services.DeadlockGraphViewportService? deadlockGraphViewportService = null,
            Core.Services.DeadlockGraphGeometryService? deadlockGraphGeometryService = null,
            Core.Services.DeadlockCanvasInteractionService? deadlockCanvasInteractionService = null,
            Core.Services.DeadlockNodeDragService? deadlockNodeDragService = null,
            Core.Services.DeadlockGraphSelectionService? deadlockGraphSelectionService = null,
            Core.Services.DeadlockGraphLayoutService? deadlockGraphLayoutService = null,
            Core.Services.DeadlockGraphEdgeService? deadlockGraphEdgeService = null,
            Core.Services.DeadlockGraphEdgeRegistryService? deadlockGraphEdgeRegistryService = null,
            Core.Services.DeadlockGraphPlacementService? deadlockGraphPlacementService = null,
            Core.Services.DeadlockPlaybackStateService? deadlockPlaybackStateService = null,
            Core.Services.DeadlockGraphVisualStateService? deadlockGraphVisualStateService = null,
            Core.Services.DeadlockStepBadgeService? deadlockStepBadgeService = null,
            Core.Services.WorkspacePanelLayoutService? workspacePanelLayoutService = null,
            Core.Services.TuningSessionActionService? tuningSessionActionService = null)
        {
            InitializeComponent();
            _temporaryFileManager = temporaryFileManager ?? new TemporaryFileManager();
            _analysisSessions = analysisSessions ?? new Core.Services.AnalysisSessionCoordinator();
            _browserLauncher = browserLauncher ?? new Core.Services.BrowserLauncher(_temporaryFileManager);
            _shellActionService = new MainWindowShellActionService(
                analysisClipboardService ?? new Core.Services.AnalysisClipboardService(),
                logFolderActionService ?? new Core.Services.LogFolderActionService(),
                _browserLauncher,
                fileAssociationRegistrationService ?? new Core.Services.FileAssociationRegistrationService());
            Core.Services.IPdfWordReportExporter effectiveReportExporter =
                pdfWordReportService ?? new Core.Services.PdfWordReportService(_temporaryFileManager);
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
            _documentRefreshActionService = documentRefreshActionService
                ?? new Core.Services.DocumentRefreshActionService();
            _planComparisonController = planComparisonController
                ?? new Core.Services.PlanComparisonController();
            _planComparisonTreeService = planComparisonTreeService
                ?? new Core.Services.PlanComparisonTreeService();
            _planComparisonTreeViewRenderer = planComparisonTreeViewRenderer
                ?? new Core.Services.PlanComparisonTreeViewRenderer();
            _mermaidDiagramService = mermaidDiagramService
                ?? new Core.Services.MermaidDiagramService();
            Core.Services.MermaidDiagramActionService effectiveMermaidDiagramActionService =
                mermaidDiagramActionService
                ?? new Core.Services.MermaidDiagramActionService(_mermaidDiagramService);
            _mermaidDiagramUiActionService =
                new MermaidDiagramUiActionService(
                    effectiveMermaidDiagramActionService,
                    _browserLauncher);
            _analysisReportController = analysisReportController
                ?? new Core.Services.AnalysisReportController(_mermaidDiagramService);
            Core.Services.HtmlReportActionService effectiveHtmlReportActionService =
                htmlReportActionService
                ?? new Core.Services.HtmlReportActionService(_analysisReportController);
            Core.Services.PortableReportActionService effectivePortableReportActionService =
                portableReportActionService
                ?? new Core.Services.PortableReportActionService(_analysisReportController);
            _fileDialogService = fileDialogService
                ?? new Core.Services.WpfFileDialogService();
            Core.Services.HtmlReportExportService effectiveHtmlReportExportService =
                htmlReportExportService
                ?? new Core.Services.HtmlReportExportService(
                    new Core.Services.HtmlReportWriter(),
                    _fileDialogService);
            Core.Services.PortableReportExportService effectivePortableReportExportService =
                portableReportExportService
                ?? new Core.Services.PortableReportExportService(
                    effectiveReportExporter,
                    _fileDialogService);
            _reportExportUiActionService = new ReportExportUiActionService(
                effectiveHtmlReportActionService,
                effectiveHtmlReportExportService,
                effectivePortableReportActionService,
                effectivePortableReportExportService,
                _browserLauncher);
            _planObfuscationExportUiActionService =
                new PlanObfuscationExportUiActionService(_fileDialogService);
            _fileOpenUiActionService =
                new FileOpenUiActionService(_fileDialogService);
            _xelDeadlockUiActionService =
                new XelDeadlockUiActionService(
                    xelReader ?? new Core.XelReader(),
                    _analysisSessions,
                    XelDeadlockSelector,
                    MainTabControl,
                    AnalyzeDeadlockXmlAsync);
            Core.Services.MissingIndexDeploymentScriptService effectiveMissingIndexDeploymentScriptService =
                missingIndexDeploymentScriptService
                ?? new Core.Services.MissingIndexDeploymentScriptService();
            _missingIndexClipboardUiActionService =
                new MissingIndexClipboardUiActionService(
                    missingIndexClipboardActionService
                    ?? new Core.Services.MissingIndexClipboardActionService(
                        effectiveMissingIndexDeploymentScriptService));
            _deadlockSelectionDetailService = deadlockSelectionDetailService
                ?? new Core.Services.DeadlockSelectionDetailService();
            _deadlockGraphViewportService = deadlockGraphViewportService
                ?? new Core.Services.DeadlockGraphViewportService();
            _deadlockGraphGeometryService = deadlockGraphGeometryService
                ?? new Core.Services.DeadlockGraphGeometryService();
            _deadlockGraphEdgeElementFactory =
                new DeadlockGraphEdgeElementFactory(_deadlockGraphGeometryService);
            _deadlockCanvasInteractionBinder =
                new DeadlockCanvasInteractionBinder(
                    deadlockCanvasInteractionService
                    ?? new Core.Services.DeadlockCanvasInteractionService());
            _deadlockNodeInteractionBinder =
                new DeadlockNodeInteractionBinder(
                    deadlockNodeDragService ?? new Core.Services.DeadlockNodeDragService(),
                    deadlockGraphSelectionService ?? new Core.Services.DeadlockGraphSelectionService());
            _deadlockGraphLayoutService = deadlockGraphLayoutService
                ?? new Core.Services.DeadlockGraphLayoutService();
            _deadlockGraphEdgeService = deadlockGraphEdgeService
                ?? new Core.Services.DeadlockGraphEdgeService();
            _deadlockGraphEdgeRegistryService = deadlockGraphEdgeRegistryService
                ?? new Core.Services.DeadlockGraphEdgeRegistryService();
            _deadlockGraphPlacementService = deadlockGraphPlacementService
                ?? new Core.Services.DeadlockGraphPlacementService();
            _deadlockGraphNodeElementFactory = new DeadlockGraphNodeElementFactory();
            _deadlockPlaybackStateService = deadlockPlaybackStateService
                ?? new Core.Services.DeadlockPlaybackStateService();
            _deadlockGraphVisualStateService = deadlockGraphVisualStateService
                ?? new Core.Services.DeadlockGraphVisualStateService();
            _deadlockGraphPlaybackVisualService =
                new DeadlockGraphPlaybackVisualService();
            _deadlockStepBadgeService = deadlockStepBadgeService
                ?? new Core.Services.DeadlockStepBadgeService();
            _workspacePanelLayoutService = workspacePanelLayoutService
                ?? new Core.Services.WorkspacePanelLayoutService();
            Core.Services.TuningSessionActionService effectiveTuningSessionActionService =
                tuningSessionActionService
                ?? new Core.Services.TuningSessionActionService(_fileDialogService);
            _planTreeService = planTreeService
                ?? new Core.Services.PlanTreeService();
            _planSelectionUiActionService = new PlanSelectionUiActionService(
                planSelectionActionService ?? new Core.Services.PlanSelectionActionService(),
                planPropertyService ?? new Core.Services.PlanPropertyService(),
                PlanPropertiesGrid);
            _planOperatorTreeViewRenderer = planOperatorTreeViewRenderer
                ?? new Core.Services.PlanOperatorTreeViewRenderer();
            Core.Services.SqlDiffService effectiveSqlDiffService = sqlDiffService
                ?? new Core.Services.SqlDiffService();
            Core.Services.SqlDiffDocumentRenderer effectiveSqlDiffDocumentRenderer =
                sqlDiffDocumentRenderer
                ?? new Core.Services.SqlDiffDocumentRenderer(effectiveSqlDiffService);
            Core.Services.SqlQuickFixService effectiveSqlQuickFixService = sqlQuickFixService
                ?? new Core.Services.SqlQuickFixService();
            _sqlDiffScrollSyncService =
                new SqlDiffScrollSyncService(OriginalSqlTextBox, RefactoredSqlTextBox);
            _sqlDiffUiActionService =
                new SqlDiffUiActionService(
                    effectiveSqlDiffService,
                    effectiveSqlDiffDocumentRenderer,
                    OriginalSqlTextBox,
                    RefactoredSqlTextBox);
            _sqlQuickFixUiActionService =
                new SqlQuickFixUiActionService(
                    this,
                    effectiveSqlQuickFixService,
                    () => _currentOriginalSql,
                    ApplySqlQuickFixResult);
            _temporaryFileManager.CleanupStaleFiles(TimeSpan.FromHours(24));
            ViewModel = new Core.ViewModels.MainViewModel(tuningSessionService);
            ViewModel.ShowMessageBox = msg => MessageBox.Show(msg);
            this.DataContext = ViewModel;
            _tuningSessionUiActionService =
                new TuningSessionUiActionService(
                    effectiveTuningSessionActionService,
                    ViewModel);
            _deadlockCanvasInteractionBinder.Attach(
                DeadlockGraphCanvas,
                DeadlockCanvasBorder,
                DeadlockScaleTransform,
                DeadlockTranslateTransform);
            this.Loaded += (s, e) => _sqlDiffScrollSyncService.Attach();
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

        private async void OpenDeadlockFile_Click(object sender, RoutedEventArgs e)
        {
            await _fileOpenUiActionService.OpenDeadlockAsync(
                AnalyzeXelFileAsync,
                AnalyzeDeadlockFile);
        }

        private async Task AnalyzeXelFileAsync(string filePath)
        {
            await _xelDeadlockUiActionService.AnalyzeXelFileAsync(filePath);
        }

        private async void XelDeadlockSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await _xelDeadlockUiActionService.HandleSelectionChangedAsync();
        }

        private void OpenPlanFile_Click(object sender, RoutedEventArgs e)
        {
            _fileOpenUiActionService.OpenPlan(AnalyzeExecutionPlanFile);
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            _fileOpenUiActionService.HandleDragEnter(e);
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            await _fileOpenUiActionService.HandleDropAsync(
                e,
                AnalyzeXelFileAsync,
                AnalyzeDeadlockFile,
                AnalyzeExecutionPlanFile);
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

            Core.Services.DeadlockPlaybackGraphState playbackState =
                _deadlockPlaybackStateService.BuildState(
                    _currentTimeline,
                    currentStep,
                    focusCritical,
                    _nodeElements.Keys,
                    _arrowCache.Keys.Select(edge => new Core.Services.DeadlockPlaybackEdgeKey(edge.Item1, edge.Item2)));

            foreach (var kvp in _nodeElements)
            {
                string id = kvp.Key;
                var el = kvp.Value;
                Core.Services.DeadlockPlaybackNodeState nodeState = playbackState.Nodes[id];
                Core.Services.DeadlockGraphNodeVisualState visualState =
                    _deadlockGraphVisualStateService.CreatePlaybackNodeState(nodeState);
                _deadlockGraphPlaybackVisualService.ApplyNodeVisualState(el, visualState);
            }

            foreach (var edge in _arrowCache)
            {
                var idPair = edge.Key;
                var visuals = edge.Value;
                var playbackEdgeKey = new Core.Services.DeadlockPlaybackEdgeKey(idPair.Item1, idPair.Item2);
                Core.Services.DeadlockPlaybackEdgeState edgeState = playbackState.Edges[playbackEdgeKey];
                Core.Services.DeadlockGraphEdgeVisualState visualState =
                    _deadlockGraphVisualStateService.CreatePlaybackEdgeState(edgeState);

                _deadlockGraphPlaybackVisualService.ApplyEdgeVisualState(visuals, visualState);

                if (!visualState.IsVisible || !visualState.BadgeStepNumber.HasValue)
                {
                    if (_stepBadges.TryGetValue(idPair, out var badge)) badge.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (!_stepBadges.TryGetValue(idPair, out var badge))
                    {
                        badge = _deadlockGraphPlaybackVisualService.CreateStepBadge();
                        _stepBadges[idPair] = badge;
                        DeadlockGraphCanvas.Children.Add(badge);
                    }
                    _deadlockGraphPlaybackVisualService.ApplyStepBadgePlacement(
                        badge,
                        _deadlockStepBadgeService.PlaceBadge(
                            visualState.BadgeStepNumber.Value,
                            visuals.Line.X1,
                            visuals.Line.Y1,
                            visuals.Line.X2,
                            visuals.Line.Y2));
                    badge.Visibility = Visibility.Visible;
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
                Core.Services.DeadlockGraphNodeVisualState resetState =
                    _deadlockGraphVisualStateService.CreateResetNodeState();
                _deadlockGraphPlaybackVisualService.ApplyNodeVisualState(el, resetState);
            }
            foreach (var edge in _arrowCache)
            {
                bool isWaitEdge = _deadlockGraphEdgeRegistryService.IsWaitEdge(
                    _edgesForDrawing,
                    edge.Key.Item1,
                    edge.Key.Item2);
                Core.Services.DeadlockGraphEdgeVisualState resetState =
                    _deadlockGraphVisualStateService.CreateResetEdgeState(isWaitEdge);
                _deadlockGraphPlaybackVisualService.ApplyEdgeVisualState(edge.Value, resetState);
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

        private void DrawDeadlockBipartiteGraph(DeadlockGraph graph)
        {
            DeadlockGraphCanvas.Children.Clear();
            _nodePositions.Clear();
            _nodeElements.Clear();
            _edgesForDrawing.Clear();
            _arrowCache.Clear();
            _resourceGroupDetails.Clear();

            var layout = _deadlockGraphLayoutService.BuildLayout(graph);
            var collapsedProcesses = layout.Processes;
            var collapsedResources = layout.Resources;

            foreach (var detail in layout.ResourceGroupDetails)
            {
                _resourceGroupDetails[detail.Key] = detail.Value;
            }

            if (collapsedProcesses.Count == 0)
            {
                var tb = new TextBlock { Text = "无有效的死锁进程数据", Margin = new Thickness(20), FontSize = 12, Foreground = Brushes.Gray };
                DeadlockGraphCanvas.Children.Add(tb);
                return;
            }

            // 还原缩放和平移，使每次打开新文件时居中
            DeadlockScaleTransform.ScaleX = 1.0;
            DeadlockScaleTransform.ScaleY = 1.0;
            DeadlockTranslateTransform.X = 0;
            DeadlockTranslateTransform.Y = 0;

            double canvasWidth = DeadlockCanvasBorder.ActualWidth > 0 ? DeadlockCanvasBorder.ActualWidth : 800;
            double canvasHeight = DeadlockCanvasBorder.ActualHeight > 0 ? DeadlockCanvasBorder.ActualHeight : 600;
            Core.Services.DeadlockGraphPlacementResult placement =
                _deadlockGraphPlacementService.PlaceNodes(
                    layout,
                    graph.VictimProcessId,
                    canvasWidth,
                    canvasHeight);

            // 3. 绘制并排版独立的进程节点（环形分布）
            foreach (Core.Services.DeadlockGraphProcessPlacement processPlacement in placement.Processes)
            {
                DrawDraggableProcessNode(
                    processPlacement.Position.X,
                    processPlacement.Position.Y,
                    processPlacement.Width,
                    processPlacement.Height,
                    processPlacement.Process.PrimaryProcess,
                    processPlacement.IsVictim,
                    processPlacement.NodeId,
                    processPlacement.Process.ThreadCount);
            }

            // 4. 绘制并排版独立的资源节点（环形分布）
            foreach (Core.Services.DeadlockGraphResourcePlacement resourcePlacement in placement.Resources)
            {
                DrawDraggableResourceNode(
                    resourcePlacement.Position.X,
                    resourcePlacement.Position.Y,
                    resourcePlacement.Width,
                    resourcePlacement.Height,
                    resourcePlacement.Resource.RawResources.First(),
                    resourcePlacement.NodeId,
                    resourcePlacement.Resource.LockCount);
            }

            foreach (Core.Services.DeadlockGraphEdge edge in _deadlockGraphEdgeService.BuildEdges(collapsedResources))
            {
                DrawArrowBetweenNodes(edge.FromId, edge.ToId, edge.Label, edge.IsWaitEdge);
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
            Canvas.SetLeft(tip, placement.TipPosition.X);
            Canvas.SetTop(tip, placement.TipPosition.Y);
            DeadlockGraphCanvas.Children.Add(tip);
        }

        private FrameworkElement DrawDraggableProcessNode(double x, double y, double w, double h, DeadlockProcess proc, bool isVictim, string id, int threadCount)
        {
            FrameworkElement card = _deadlockGraphNodeElementFactory.CreateProcessNode(
                w,
                h,
                proc,
                isVictim,
                id,
                threadCount);

            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            DeadlockGraphCanvas.Children.Add(card);

            _nodeElements[id] = card;
            _nodePositions[id] = new Point(x, y);

            AttachNodeInteraction(card, id);

            return card;
        }

        private FrameworkElement DrawDraggableResourceNode(double x, double y, double w, double h, LockResource res, string id, int lockCount)
        {
            FrameworkElement container = _deadlockGraphNodeElementFactory.CreateResourceNode(
                w,
                h,
                res,
                id,
                lockCount);

            Canvas.SetLeft(container, x);
            Canvas.SetTop(container, y);
            DeadlockGraphCanvas.Children.Add(container);

            _nodeElements[id] = container;
            _nodePositions[id] = new Point(x, y);

            AttachNodeInteraction(container, id);

            return container;
        }

        private void AttachNodeInteraction(FrameworkElement element, string id)
        {
            _deadlockNodeInteractionBinder.Attach(
                element,
                id,
                DeadlockGraphCanvas,
                _nodePositions,
                _resourceGroupDetails,
                DeadlockProcessesList,
                DeadlockResourcesList,
                UpdateConnectionsForNode);
        }

        private void UpdateConnectionsForNode(string movedId)
        {
            IReadOnlyList<Core.Services.DeadlockGraphEdge> edgesToUpdate =
                _deadlockGraphEdgeRegistryService.FindEdgesForNode(_edgesForDrawing, movedId);

            foreach (var edge in edgesToUpdate)
            {
                var key = (edge.FromId, edge.ToId);
                if (_arrowCache.TryGetValue(key, out var cached))
                {
                    Core.Services.DeadlockConnectionPoints points =
                        CalculateConnectionPoints(edge.FromId, edge.ToId);
                    _deadlockGraphEdgeElementFactory.UpdateEdge(cached, points);
                }
            }
        }

        private Core.Services.DeadlockConnectionPoints CalculateConnectionPoints(string fromId, string toId)
        {
            return _deadlockGraphGeometryService.CalculateConnectionPoints(
                _nodePositions,
                fromId,
                toId);
        }

        private void DrawArrowBetweenNodes(string fromId, string toId, string label, bool isWaitEdge)
        {
            Core.Services.DeadlockConnectionPoints points =
                CalculateConnectionPoints(fromId, toId);

            DeadlockGraphEdgeElements elements =
                _deadlockGraphEdgeElementFactory.CreateEdge(
                    points,
                    label,
                    isWaitEdge);

            DeadlockGraphCanvas.Children.Add(elements.Line);
            DeadlockGraphCanvas.Children.Add(elements.ArrowHead);
            DeadlockGraphCanvas.Children.Add(elements.Label);

            var key = (fromId, toId);
            _arrowCache[key] = elements;
            _edgesForDrawing.Add(new Core.Services.DeadlockGraphEdge(fromId, toId, label, isWaitEdge));
        }

        private void BuildPlanVisualTree(XDocument doc, XNamespace ns)
        {
            PlanVisualTree.ItemsSource = _planTreeService.BuildVisualTree(doc, ns);
        }

        // 简单构建执行计划 TreeView 节点 (参考 Plan Explorer 左侧树)
        private TreeViewItem? BuildPlanTreeView(XDocument doc, XNamespace ns)
        {
            Core.Services.PlanOperatorTreeNode? root = _planTreeService.BuildOperatorTree(doc, ns);
            return root == null ? null : _planOperatorTreeViewRenderer.Render(root);
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
            Core.Services.PlanComparisonTreeResult displayTree =
                _planComparisonTreeService.BuildTree(comparison);

            if (displayTree.PlanA != null)
            {
                PlanATreeView.Items.Add(
                    _planComparisonTreeViewRenderer.Render(displayTree.PlanA));
            }

            if (displayTree.PlanB != null)
            {
                PlanBTreeView.Items.Add(
                    _planComparisonTreeViewRenderer.Render(displayTree.PlanB));
            }
        }

        #endregion

        #region 其他功能

        private void ExportObfuscatedPlan_Click(object sender, RoutedEventArgs e)
        {
            _planObfuscationExportUiActionService.Export(
                ViewModel.CurrentPlanDoc,
                status => StatusTextBlock.Text = status);
        }

        private void GenerateHtmlReport_Click(object sender, RoutedEventArgs e)
        {
            _reportExportUiActionService.GenerateHtmlReport(
                MainTabControl.SelectedIndex,
                ViewModel.CurrentDeadlockDoc,
                ViewModel.CurrentDeadlockFilePath,
                ViewModel.DeadlockPatternText,
                ViewModel.CurrentPlanDoc,
                ViewModel.CurrentPlanFilePath,
                _showplanNs,
                ViewModel.MissingIndexes);
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
            _reportExportUiActionService.ExportPortableReport(
                MainTabControl.SelectedIndex,
                ViewModel.CurrentDeadlockFilePath,
                DeadlockPatternsListBox.ItemsSource?.OfType<DeadlockPattern>(),
                ViewModel.DeadlockPatternText,
                ViewModel.CurrentPlanFilePath,
                ViewModel.PlanWarningsText,
                DeadlockCanvasBorder,
                extension,
                filter);
        }

        private void CopyAnalysisResult_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.CopyAnalysisResult(
                MainTabControl.SelectedIndex,
                ViewModel.DeadlockPatternText,
                ViewModel.PlanWarningsText);
        }

        private void CopyRefactoredSql_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.CopyRefactoredSql(_currentRefactoredSql);
        }

        private void CompareSql_Click(object sender, RoutedEventArgs e)
        {
            Core.Services.SqlComparePanelLayout layout =
                _workspacePanelLayoutService.ToggleSqlCompare(OriginalSqlCol.Width);

            OriginalSqlCol.Width = layout.OriginalSqlWidth;
            SqlSplitterCol.Width = layout.SplitterWidth;
            SqlGridSplitter.Visibility = layout.SplitterVisibility;
            BtnCompareSql.Content = layout.ButtonContent;
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
            _shellActionService.OpenLogsFolder();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.ShowAboutAndRegisterAssociations();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.ExitApplication();
        }

        #endregion

        #region 事件处理 (补充)

        private void DeadlockProcessesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeadlockProcessesList.SelectedItem is DeadlockProcess proc)
            {
                ViewModel.DeadlockPatternText =
                    _deadlockSelectionDetailService.BuildProcessDetail(proc);
            }
        }

        private void DeadlockResourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeadlockResourcesList.SelectedItem is LockResource res)
            {
                ViewModel.DeadlockPatternText =
                    _deadlockSelectionDetailService.BuildResourceDetail(res);
            }
        }

        private void ToggleLeft_Click(object sender, RoutedEventArgs e)
        {
            if (DeadlockLeftColumn == null) return;

            Core.Services.SidePanelLayout layout =
                _workspacePanelLayoutService.ToggleDeadlockLeftPanel(DeadlockLeftColumn.Width);
            DeadlockLeftColumn.Width = layout.Width;
            ToggleLeftBtn.Content = layout.ButtonContent;
        }

        private void ToggleRight_Click(object sender, RoutedEventArgs e)
        {
            if (DeadlockRightColumn == null) return;

            Core.Services.SidePanelLayout layout =
                _workspacePanelLayoutService.ToggleDeadlockRightPanel(DeadlockRightColumn.Width);
            DeadlockRightColumn.Width = layout.Width;
            ToggleRightBtn.Content = layout.ButtonContent;
        }

        private void ZoomToFitDeadlock_Click(object sender, RoutedEventArgs e)
        {
            DoZoomToFitDeadlock();
        }

        private void DoZoomToFitDeadlock()
        {
            Core.Services.DeadlockViewportState? viewport =
                _deadlockGraphViewportService.CalculateZoomToFit(
                    _nodePositions,
                    DeadlockCanvasBorder.ActualWidth,
                    DeadlockCanvasBorder.ActualHeight);

            if (viewport == null)
            {
                return;
            }

            DeadlockScaleTransform.ScaleX = viewport.Scale;
            DeadlockScaleTransform.ScaleY = viewport.Scale;
            DeadlockTranslateTransform.X = viewport.TranslateX;
            DeadlockTranslateTransform.Y = viewport.TranslateY;
        }

        private void DeadlockPatternsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeadlockPatternsListBox.SelectedItem is DeadlockPattern pattern)
            {
                ViewModel.DeadlockPatternText =
                    _deadlockSelectionDetailService.BuildPatternDetail(pattern);
            }
        }

        #region 折叠面板事件处理
        private GridLength _leftColWidth = new GridLength(320);
        private GridLength _rightColWidth = new GridLength(280);

        private void LeftPanel_Expanded(object sender, RoutedEventArgs e)
        {
            if (PlanContentGrid != null && PlanContentGrid.ColumnDefinitions.Count > 0)
                PlanContentGrid.ColumnDefinitions[0].Width =
                    _workspacePanelLayoutService.ExpandCollapsiblePanel(_leftColWidth);
        }

        private void LeftPanel_Collapsed(object sender, RoutedEventArgs e)
        {
            if (PlanContentGrid != null && PlanContentGrid.ColumnDefinitions.Count > 0)
            {
                Core.Services.CollapsiblePanelLayout layout =
                    _workspacePanelLayoutService.CollapseCollapsiblePanel(
                        PlanContentGrid.ColumnDefinitions[0].Width);
                _leftColWidth = layout.StoredWidth;
                PlanContentGrid.ColumnDefinitions[0].Width = layout.AppliedWidth;
            }
        }

        private void RightPanel_Expanded(object sender, RoutedEventArgs e)
        {
            if (PlanContentGrid != null && PlanContentGrid.ColumnDefinitions.Count > 4)
                PlanContentGrid.ColumnDefinitions[4].Width =
                    _workspacePanelLayoutService.ExpandCollapsiblePanel(_rightColWidth);
        }

        private void RightPanel_Collapsed(object sender, RoutedEventArgs e)
        {
            if (PlanContentGrid != null && PlanContentGrid.ColumnDefinitions.Count > 4)
            {
                Core.Services.CollapsiblePanelLayout layout =
                    _workspacePanelLayoutService.CollapseCollapsiblePanel(
                        PlanContentGrid.ColumnDefinitions[4].Width);
                _rightColWidth = layout.StoredWidth;
                PlanContentGrid.ColumnDefinitions[4].Width = layout.AppliedWidth;
            }
        }
        #endregion


        private void PlanOperatorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _planSelectionUiActionService.SelectFromOperatorTreeItem(e.NewValue);
        }

        private void RefreshDeadlockGraph_Click(object sender, RoutedEventArgs e)
        {
            Core.Services.DocumentRefreshActionResult result =
                _documentRefreshActionService.BuildDeadlockRefresh(ViewModel.CurrentDeadlockFilePath);

            if (result.Status == Core.Services.DocumentRefreshActionStatus.MissingFile)
            {
                MessageBox.Show(result.UserMessage, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AnalyzeFile(result.FilePath);
        }

        private void CopyDeadlockMermaid_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.CopyDeadlockDiagram(ViewModel.CurrentDeadlockDoc);
        }

        private void RefreshPlanGraph_Click(object sender, RoutedEventArgs e)
        {
            Core.Services.DocumentRefreshActionResult result =
                _documentRefreshActionService.BuildPlanRefresh(ViewModel.CurrentPlanFilePath);

            if (result.Status == Core.Services.DocumentRefreshActionStatus.MissingFile)
            {
                MessageBox.Show(result.UserMessage, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AnalyzeFile(result.FilePath);
        }

        private void CopyPlanMermaid_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.CopyPlanDiagram(
                ViewModel.CurrentPlanDoc,
                _showplanNs);
        }

        private void PlanVisualTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _planSelectionUiActionService.SelectFromVisualTreeNode(e.NewValue);
        }

        // Nodify 节点选中 -> 同步到主右侧属性面板 (Plan Explorer 风格)
        private void PlanNodifyGraph_NodeSelected(object sender, PlanNodeViewModel node)
        {
            _planSelectionUiActionService.SelectFromGraphNode(node);
        }

        private void PlanNodifyGraph_NodeDoubleClicked(object sender, PlanNodeViewModel node)
        {
            _planSelectionUiActionService.SelectFromGraphNode(node);
        }

        private void OpenPlanMermaidInBrowser_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.OpenPlanDiagram(
                ViewModel.CurrentPlanDoc,
                _showplanNs);
        }

        private void OpenDeadlockMermaidInBrowser_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.OpenDeadlockDiagram(ViewModel.CurrentDeadlockDoc);
        }

        #endregion

        private void PlanNodifyGraph_Loaded(object sender, RoutedEventArgs e)
        {

        }

        // --- 调优历史与 A/B 并排对比事件处理器 ---
        private async void TuningHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Core.ViewModels.PlanSnapshot? snapshot =
                _tuningSessionUiActionService.GetSelectedHistorySnapshot(
                    TuningHistoryListView.SelectedItem);

            if (snapshot != null)
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
            _tuningSessionUiActionService.SaveSession();
        }

        private void LoadSession_Click(object sender, RoutedEventArgs e)
        {
            _tuningSessionUiActionService.LoadSession();
        }

        private void SwapPlanAB_Click(object sender, RoutedEventArgs e)
        {
            _tuningSessionUiActionService.SwapPlans();
        }

        private void StatisticsHistogramView_Loaded(object sender, RoutedEventArgs e)
        {

        }

        #region 可视化看板与交互展示 (GUI Dashboard Integration & Interactive Visualization)

        private void UpdateSqlDiffViews()
        {
            _sqlDiffUiActionService.RenderDiff(
                _currentOriginalSql,
                _currentRefactoredSql,
                _sqlQuickFixUiActionService.CreateLightbulbButton);
        }

        private void ApplySqlQuickFixResult(SqlQuickFixAppliedResult result)
        {
            _currentOriginalSql = result.RewrittenSql;
            _currentRefactoredSql = result.RewrittenSql;
            UpdateSqlDiffViews();
            PlanStatementTextBox.Text = result.StatementPreview;
        }
        private void CopyIndexDdl_Click(object sender, RoutedEventArgs e)
        {
            _missingIndexClipboardUiActionService.CopyCreateScript(sender);
        }

        private void CopyRollbackDdl_Click(object sender, RoutedEventArgs e)
        {
            _missingIndexClipboardUiActionService.CopyRollbackScript(sender);
        }

        private void CopyDeploymentBundle_Click(object sender, RoutedEventArgs e)
        {
            _missingIndexClipboardUiActionService.CopyDeploymentBundle(sender);
        }

        private static void CopyMissingIndexClipboardResult(
            Core.Services.MissingIndexClipboardActionResult result)
        {
            if (result.Status == Core.Services.MissingIndexClipboardActionStatus.Ready)
            {
                Clipboard.SetText(result.Text);
                MessageBox.Show(result.SuccessMessage, "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion
    }
}
