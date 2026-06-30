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
        private readonly DocumentRefreshUiActionService _documentRefreshUiActionService;
        private readonly PlanComparisonUiActionService _planComparisonUiActionService;
        private readonly Core.Services.MermaidDiagramService _mermaidDiagramService;
        private readonly MermaidDiagramUiActionService _mermaidDiagramUiActionService;
        private readonly Core.Services.AnalysisReportController _analysisReportController;
        private readonly ReportExportUiActionService _reportExportUiActionService;
        private readonly PlanObfuscationExportUiActionService _planObfuscationExportUiActionService;
        private readonly FileOpenUiActionService _fileOpenUiActionService;
        private readonly XelDeadlockUiActionService _xelDeadlockUiActionService;
        private readonly AnalysisResultsUiActionService _analysisResultsUiActionService;
        private readonly DeadlockAnalysisUiActionService _deadlockAnalysisUiActionService;
        private readonly Core.Services.IFileDialogService _fileDialogService;
        private readonly MissingIndexClipboardUiActionService _missingIndexClipboardUiActionService;
        private readonly DeadlockSelectionUiActionService _deadlockSelectionUiActionService;
        private readonly DeadlockViewportUiActionService _deadlockViewportUiActionService;
        private readonly DeadlockCanvasInteractionBinder _deadlockCanvasInteractionBinder;
        private readonly DeadlockGraphRenderUiActionService _deadlockGraphRenderUiActionService;
        private readonly DeadlockGraphElementUiActionService _deadlockGraphElementUiActionService;
        private readonly DeadlockPlaybackUiActionService _deadlockPlaybackUiActionService;
        private readonly WorkspacePanelUiActionService _workspacePanelUiActionService;
        private readonly TuningSessionUiActionService _tuningSessionUiActionService;
        private readonly PlanAnalysisUiActionService _planAnalysisUiActionService;
        private readonly PlanSelectionUiActionService _planSelectionUiActionService;
        private readonly SqlDiffScrollSyncService _sqlDiffScrollSyncService;
        private readonly SqlDiffUiActionService _sqlDiffUiActionService;
        private readonly SqlQuickFixUiActionService _sqlQuickFixUiActionService;
        private readonly PlanStatisticsUiActionService _planStatisticsUiActionService;

        private Dictionary<string, FrameworkElement> _nodeElements = new Dictionary<string, FrameworkElement>();
        private Dictionary<(string, string), DeadlockGraphEdgeElements> _arrowCache = new Dictionary<(string, string), DeadlockGraphEdgeElements>();
        private List<Core.Services.DeadlockGraphEdge> _edgesForDrawing = new();

        private Dictionary<(string, string), Border> _stepBadges = new Dictionary<(string, string), Border>();

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _shellActionService.HandleTitleBarMouseLeftButtonDown(e.ClickCount);
        }

        private void Minimize_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _shellActionService.Minimize();
        }

        private void Maximize_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _shellActionService.ToggleMaximize();
        }

        private void Close_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _shellActionService.Close();
        }

        private void ThemeToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _shellActionService.SetTheme();
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
            ViewModel = new Core.ViewModels.MainViewModel(tuningSessionService);
            ViewModel.ShowMessageBox = msg => MessageBox.Show(msg);
            this.DataContext = ViewModel;
            _temporaryFileManager = temporaryFileManager ?? new TemporaryFileManager();
            _analysisSessions = analysisSessions ?? new Core.Services.AnalysisSessionCoordinator();
            _browserLauncher = browserLauncher ?? new Core.Services.BrowserLauncher(_temporaryFileManager);
            _shellActionService = new MainWindowShellActionService(
                analysisClipboardService ?? new Core.Services.AnalysisClipboardService(),
                logFolderActionService ?? new Core.Services.LogFolderActionService(),
                _browserLauncher,
                fileAssociationRegistrationService ?? new Core.Services.FileAssociationRegistrationService(),
                this,
                MainTabControl,
                ViewModel,
                () => ThemeToggle.IsChecked == true);
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
            _documentRefreshUiActionService =
                new DocumentRefreshUiActionService(
                    documentRefreshActionService
                    ?? new Core.Services.DocumentRefreshActionService(),
                    AnalyzeFile,
                    ViewModel);
            Core.Services.PlanComparisonController effectivePlanComparisonController =
                planComparisonController
                ?? new Core.Services.PlanComparisonController();
            Core.Services.PlanComparisonTreeService effectivePlanComparisonTreeService =
                planComparisonTreeService
                ?? new Core.Services.PlanComparisonTreeService();
            Core.Services.PlanComparisonTreeViewRenderer effectivePlanComparisonTreeViewRenderer =
                planComparisonTreeViewRenderer
                ?? new Core.Services.PlanComparisonTreeViewRenderer();
            _mermaidDiagramService = mermaidDiagramService
                ?? new Core.Services.MermaidDiagramService();
            Core.Services.MermaidDiagramActionService effectiveMermaidDiagramActionService =
                mermaidDiagramActionService
                ?? new Core.Services.MermaidDiagramActionService(_mermaidDiagramService);
            _mermaidDiagramUiActionService =
                new MermaidDiagramUiActionService(
                    effectiveMermaidDiagramActionService,
                    _browserLauncher,
                    ViewModel,
                    _showplanNs);
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
                _browserLauncher,
                ViewModel,
                MainTabControl,
                DeadlockPatternsListBox,
                DeadlockCanvasBorder,
                _showplanNs);
            _planObfuscationExportUiActionService =
                new PlanObfuscationExportUiActionService(_fileDialogService);
            _fileOpenUiActionService =
                new FileOpenUiActionService(_fileDialogService, _documentOpenService);
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
            Core.Services.DeadlockSelectionDetailService effectiveDeadlockSelectionDetailService =
                deadlockSelectionDetailService
                ?? new Core.Services.DeadlockSelectionDetailService();
            Core.Services.DeadlockGraphViewportService effectiveDeadlockGraphViewportService =
                deadlockGraphViewportService
                ?? new Core.Services.DeadlockGraphViewportService();
            Core.Services.DeadlockGraphGeometryService effectiveDeadlockGraphGeometryService =
                deadlockGraphGeometryService
                ?? new Core.Services.DeadlockGraphGeometryService();
            DeadlockGraphEdgeElementFactory effectiveDeadlockGraphEdgeElementFactory =
                new(effectiveDeadlockGraphGeometryService);
            _deadlockCanvasInteractionBinder =
                new DeadlockCanvasInteractionBinder(
                    deadlockCanvasInteractionService
                    ?? new Core.Services.DeadlockCanvasInteractionService());
            DeadlockNodeInteractionBinder effectiveDeadlockNodeInteractionBinder =
                new(
                    deadlockNodeDragService ?? new Core.Services.DeadlockNodeDragService(),
                    deadlockGraphSelectionService ?? new Core.Services.DeadlockGraphSelectionService());
            Core.Services.DeadlockGraphLayoutService effectiveDeadlockGraphLayoutService =
                deadlockGraphLayoutService
                ?? new Core.Services.DeadlockGraphLayoutService();
            Core.Services.DeadlockGraphEdgeService effectiveDeadlockGraphEdgeService =
                deadlockGraphEdgeService
                ?? new Core.Services.DeadlockGraphEdgeService();
            Core.Services.DeadlockGraphEdgeRegistryService effectiveDeadlockGraphEdgeRegistryService =
                deadlockGraphEdgeRegistryService
                ?? new Core.Services.DeadlockGraphEdgeRegistryService();
            Core.Services.DeadlockGraphPlacementService effectiveDeadlockGraphPlacementService =
                deadlockGraphPlacementService
                ?? new Core.Services.DeadlockGraphPlacementService();
            DeadlockGraphNodeElementFactory effectiveDeadlockGraphNodeElementFactory = new();
            Core.Services.DeadlockPlaybackStateService effectiveDeadlockPlaybackStateService =
                deadlockPlaybackStateService
                ?? new Core.Services.DeadlockPlaybackStateService();
            Core.Services.DeadlockGraphVisualStateService effectiveDeadlockGraphVisualStateService =
                deadlockGraphVisualStateService
                ?? new Core.Services.DeadlockGraphVisualStateService();
            DeadlockGraphPlaybackVisualService effectiveDeadlockGraphPlaybackVisualService =
                new();
            Core.Services.DeadlockStepBadgeService effectiveDeadlockStepBadgeService =
                deadlockStepBadgeService
                ?? new Core.Services.DeadlockStepBadgeService();
            Core.Services.WorkspacePanelLayoutService effectiveWorkspacePanelLayoutService =
                workspacePanelLayoutService
                ?? new Core.Services.WorkspacePanelLayoutService();
            Core.Services.TuningSessionActionService effectiveTuningSessionActionService =
                tuningSessionActionService
                ?? new Core.Services.TuningSessionActionService(_fileDialogService);
            Core.Services.PlanTreeService effectivePlanTreeService = planTreeService
                ?? new Core.Services.PlanTreeService();
            _planSelectionUiActionService = new PlanSelectionUiActionService(
                planSelectionActionService ?? new Core.Services.PlanSelectionActionService(),
                planPropertyService ?? new Core.Services.PlanPropertyService(),
                PlanPropertiesGrid,
                () => PlanOperatorTree.SelectedItem,
                () => PlanVisualTree.SelectedItem);
            Core.Services.PlanOperatorTreeViewRenderer effectivePlanOperatorTreeViewRenderer =
                planOperatorTreeViewRenderer
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
                    RefactoredSqlTextBox,
                    PlanStatementTextBox);
            _sqlQuickFixUiActionService =
                new SqlQuickFixUiActionService(
                    this,
                    effectiveSqlQuickFixService,
                    () => _sqlDiffUiActionService.CurrentOriginalSql,
                    _sqlDiffUiActionService.ApplyQuickFixResult);
            _planStatisticsUiActionService =
                new PlanStatisticsUiActionService(StatisticsHistogramView);
            _temporaryFileManager.CleanupStaleFiles(TimeSpan.FromHours(24));
            _analysisResultsUiActionService =
                new AnalysisResultsUiActionService(
                    ViewModel,
                    DeadlockGraphCanvas,
                    DeadlockProcessesList,
                    DeadlockResourcesList,
                    DeadlockPatternsListBox,
                    PlanOperatorTree,
                    StatusTextBlock);
            _deadlockAnalysisUiActionService =
                new DeadlockAnalysisUiActionService(
                    ViewModel,
                    DeadlockProcessesList,
                    DeadlockResourcesList,
                    DeadlockPatternsListBox,
                    DeadlockGraphCanvas,
                    PlaybackControl,
                    MainTabControl,
                    BuildDeadlockWaitForTree,
                    (s, e) => UpdatePlaybackGraphVisibility(),
                    _stepBadges);
            _deadlockPlaybackUiActionService =
                new DeadlockPlaybackUiActionService(
                    effectiveDeadlockPlaybackStateService,
                    effectiveDeadlockGraphVisualStateService,
                    effectiveDeadlockGraphPlaybackVisualService,
                    effectiveDeadlockStepBadgeService,
                    effectiveDeadlockGraphEdgeRegistryService,
                    DeadlockGraphCanvas,
                    PlaybackControl);
            _deadlockGraphElementUiActionService =
                new DeadlockGraphElementUiActionService(
                    effectiveDeadlockGraphNodeElementFactory,
                    effectiveDeadlockGraphEdgeElementFactory,
                    effectiveDeadlockNodeInteractionBinder,
                    effectiveDeadlockGraphEdgeRegistryService,
                    effectiveDeadlockGraphGeometryService,
                    DeadlockGraphCanvas,
                    DeadlockProcessesList,
                    DeadlockResourcesList,
                    _nodePositions,
                    _nodeElements,
                    _edgesForDrawing,
                    _arrowCache,
                    _resourceGroupDetails);
            _deadlockViewportUiActionService =
                new DeadlockViewportUiActionService(
                    effectiveDeadlockGraphViewportService,
                    DeadlockCanvasBorder,
                    DeadlockScaleTransform,
                    DeadlockTranslateTransform,
                    _nodePositions);
            _planAnalysisUiActionService =
                new PlanAnalysisUiActionService(
                    ViewModel,
                    effectivePlanTreeService,
                    effectivePlanOperatorTreeViewRenderer,
                    PlanXmlTextBox,
                    PlanStatementTextBox,
                    PlanWarningsTextBox,
                    PlanOperatorTree,
                    PlanVisualTree,
                    PlanNodifyGraph,
                    MainTabControl,
                    PlanGraphTabControl);
            _planComparisonUiActionService =
                new PlanComparisonUiActionService(
                    effectivePlanComparisonController,
                    effectivePlanComparisonTreeService,
                    effectivePlanComparisonTreeViewRenderer,
                    PlanATreeView,
                    PlanBTreeView);
            _deadlockSelectionUiActionService =
                new DeadlockSelectionUiActionService(
                    effectiveDeadlockSelectionDetailService,
                    ViewModel,
                    () => DeadlockProcessesList.SelectedItem,
                    () => DeadlockResourcesList.SelectedItem,
                    () => DeadlockPatternsListBox.SelectedItem);
            _workspacePanelUiActionService =
                new WorkspacePanelUiActionService(
                    effectiveWorkspacePanelLayoutService,
                    OriginalSqlCol,
                    SqlSplitterCol,
                    SqlGridSplitter,
                    BtnCompareSql,
                    DeadlockLeftColumn,
                    DeadlockRightColumn,
                    ToggleLeftBtn,
                    ToggleRightBtn,
                    PlanContentGrid);
            _tuningSessionUiActionService =
                new TuningSessionUiActionService(
                    effectiveTuningSessionActionService,
                    ViewModel,
                    () => TuningHistoryListView.SelectedItem);
            _deadlockCanvasInteractionBinder.Attach(
                DeadlockGraphCanvas,
                DeadlockCanvasBorder,
                DeadlockScaleTransform,
                DeadlockTranslateTransform);
            _deadlockGraphRenderUiActionService =
                new DeadlockGraphRenderUiActionService(
                    effectiveDeadlockGraphLayoutService,
                    effectiveDeadlockGraphPlacementService,
                    effectiveDeadlockGraphEdgeService,
                    DeadlockGraphCanvas,
                    DeadlockCanvasBorder,
                    DeadlockScaleTransform,
                    DeadlockTranslateTransform,
                    _nodePositions,
                    _nodeElements,
                    _edgesForDrawing,
                    _arrowCache,
                    _resourceGroupDetails,
                    _deadlockGraphElementUiActionService.DrawProcessNode,
                    _deadlockGraphElementUiActionService.DrawResourceNode,
                    _deadlockGraphElementUiActionService.DrawEdge);
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

                DeadlockAnalysisUiResult uiResult =
                    _deadlockAnalysisUiActionService.Apply(documentResult);
                _deadlockPlaybackUiActionService.SetCurrentPlayback(
                    uiResult.Timeline,
                    uiResult.PlaybackViewModel);
                UpdatePlaybackGraphVisibility();
                StatusTextBlock.Text = uiResult.StatusText;
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
                Logger.Info($"[ExecutionPlan] Mermaid length: {result.Mermaid.Length} characters");
                PlanAnalysisUiResult uiResult =
                    _planAnalysisUiActionService.Apply(documentResult);
                _sqlDiffUiActionService.SetSql(
                    uiResult.QueryText,
                    uiResult.RefactoredSql,
                    _sqlQuickFixUiActionService.CreateLightbulbButton);
                try
                {
                    _planStatisticsUiActionService.LoadFromPlan(doc, _showplanNs);
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
            _deadlockPlaybackUiActionService.UpdateGraphVisibility(
                PlaybackModeToggle.IsChecked == true,
                _nodeElements,
                _arrowCache,
                _stepBadges);
        }

        private void PlaybackModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            _deadlockPlaybackUiActionService.ShowPlayback(
                UpdatePlaybackGraphVisibility);
        }

        private void PlaybackModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _deadlockPlaybackUiActionService.HidePlayback(
                _nodeElements,
                _arrowCache,
                _edgesForDrawing,
                _stepBadges.Values);
        }

        private void BuildDeadlockWaitForTree(DeadlockGraph graph)
        {
            DrawDeadlockBipartiteGraph(graph);
            Dispatcher.BeginInvoke(
                new Action(_deadlockViewportUiActionService.ZoomToFit),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private readonly Dictionary<string, Point> _nodePositions = new();
        private readonly Dictionary<string, (string LockType, string ObjectName)> _resourceGroupDetails = new();

        private void DrawDeadlockBipartiteGraph(DeadlockGraph graph)
        {
            _deadlockGraphRenderUiActionService.Render(graph);
        }

        private void RefreshABCompareTrees()
        {
            _planComparisonUiActionService.RefreshCompareTrees(
                ViewModel.PlanA,
                ViewModel.PlanB,
                _showplanNs);
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
            _reportExportUiActionService.GenerateHtmlReport();
        }

        private void ExportToPdf_Click(object sender, RoutedEventArgs e)
        {
            _reportExportUiActionService.ExportPdfReport();
        }

        private void ExportToWord_Click(object sender, RoutedEventArgs e)
        {
            _reportExportUiActionService.ExportWordReport();
        }

        private void CopyAnalysisResult_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.CopyAnalysisResult();
        }

        private void CopyRefactoredSql_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.CopyRefactoredSql(_sqlDiffUiActionService.CurrentRefactoredSql);
        }

        private void CompareSql_Click(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ToggleSqlCompare();
        }

        private void ClearResults_Click(object sender, RoutedEventArgs e)
        {
            _analysisResultsUiActionService.ClearResults();
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
            _deadlockSelectionUiActionService.SelectCurrentProcess();
        }

        private void DeadlockResourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _deadlockSelectionUiActionService.SelectCurrentResource();
        }

        private void ToggleLeft_Click(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ToggleDeadlockLeftPanel();
        }

        private void ToggleRight_Click(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ToggleDeadlockRightPanel();
        }

        private void ZoomToFitDeadlock_Click(object sender, RoutedEventArgs e)
        {
            _deadlockViewportUiActionService.ZoomToFit();
        }

        private void DeadlockPatternsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _deadlockSelectionUiActionService.SelectCurrentPattern();
        }

        #region 折叠面板事件处理
        private void LeftPanel_Expanded(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ExpandPlanLeftPanel();
        }

        private void LeftPanel_Collapsed(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.CollapsePlanLeftPanel();
        }

        private void RightPanel_Expanded(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ExpandPlanRightPanel();
        }

        private void RightPanel_Collapsed(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.CollapsePlanRightPanel();
        }
        #endregion


        private void PlanOperatorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _planSelectionUiActionService.SelectCurrentOperatorTreeItem();
        }

        private void RefreshDeadlockGraph_Click(object sender, RoutedEventArgs e)
        {
            _documentRefreshUiActionService.RefreshDeadlockGraph();
        }

        private void CopyDeadlockMermaid_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.CopyDeadlockDiagram();
        }

        private void RefreshPlanGraph_Click(object sender, RoutedEventArgs e)
        {
            _documentRefreshUiActionService.RefreshPlanGraph();
        }

        private void CopyPlanMermaid_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.CopyPlanDiagram();
        }

        private void PlanVisualTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _planSelectionUiActionService.SelectCurrentVisualTreeNode();
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
            _mermaidDiagramUiActionService.OpenPlanDiagram();
        }

        private void OpenDeadlockMermaidInBrowser_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.OpenDeadlockDiagram();
        }

        #endregion


        // --- 调优历史与 A/B 并排对比事件处理器 ---
        private async void TuningHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await _tuningSessionUiActionService.OpenSelectedHistorySnapshotAsync(
                _analysisSessions,
                AnalyzeExecutionPlanDocumentAsync);
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


        #region 可视化看板与交互展示 (GUI Dashboard Integration & Interactive Visualization)

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

        #endregion
    }
}
