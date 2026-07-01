using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
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
        private readonly DocumentAnalysisUiActionService _documentAnalysisUiActionService;

        private readonly DeadlockGraphUiState _deadlockGraphState = new();
    }
}
