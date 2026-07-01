using System;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Services;
using MessageBox = System.Windows.MessageBox;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
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
                    StatusTextBlock,
                    _deadlockGraphState);
            _deadlockAnalysisUiActionService =
                new DeadlockAnalysisUiActionService(
                    ViewModel,
                    DeadlockProcessesList,
                    DeadlockResourcesList,
                    DeadlockPatternsListBox,
                    DeadlockGraphCanvas,
                    PlaybackControl,
                    MainTabControl,
                    RenderDeadlockGraphAndZoom,
                    (s, e) => UpdatePlaybackGraphVisibility(),
                    _deadlockGraphState.StepBadges);
            _deadlockPlaybackUiActionService =
                new DeadlockPlaybackUiActionService(
                    effectiveDeadlockPlaybackStateService,
                    effectiveDeadlockGraphVisualStateService,
                    effectiveDeadlockGraphPlaybackVisualService,
                    effectiveDeadlockStepBadgeService,
                    effectiveDeadlockGraphEdgeRegistryService,
                    DeadlockGraphCanvas,
                    PlaybackControl,
                    _deadlockGraphState);
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
                    _deadlockGraphState);
            _deadlockViewportUiActionService =
                new DeadlockViewportUiActionService(
                    effectiveDeadlockGraphViewportService,
                    DeadlockCanvasBorder,
                    DeadlockScaleTransform,
                    DeadlockTranslateTransform,
                    _deadlockGraphState.NodePositions);
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
                    ViewModel,
                    MainTabControl,
                    PlanATreeView,
                    PlanBTreeView,
                    _showplanNs);
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
                    _deadlockGraphState,
                    _deadlockGraphElementUiActionService.DrawProcessNode,
                    _deadlockGraphElementUiActionService.DrawResourceNode,
                    _deadlockGraphElementUiActionService.DrawEdge,
                    action => Dispatcher.BeginInvoke(
                        action,
                        System.Windows.Threading.DispatcherPriority.Loaded),
                    _deadlockViewportUiActionService.ZoomToFit);
            _documentAnalysisUiActionService =
                new DocumentAnalysisUiActionService(
                    _analysisSessions,
                    ViewModel,
                    _documentOpenService,
                    _deadlockDocumentController,
                    _planDocumentController,
                    _deadlockAnalysisUiActionService,
                    _deadlockPlaybackUiActionService,
                    _planAnalysisUiActionService,
                    _sqlDiffUiActionService,
                    _sqlQuickFixUiActionService,
                    _planStatisticsUiActionService,
                    StatusTextBlock,
                    _showplanNs,
                    UpdatePlaybackGraphVisibility,
                    _xelDeadlockUiActionService.AnalyzeXelFileAsync);
            this.Loaded += (s, e) => _sqlDiffScrollSyncService.Attach();
            this.Closed += (s, e) => _analysisSessions.CancelCurrent();

            ViewModel.PropertyChanged += _planComparisonUiActionService.HandleViewModelPropertyChanged;
        }
    }
}
