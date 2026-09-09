using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DocumentAnalysisUiActionService
    {
        private readonly Core.Services.AnalysisSessionCoordinator _analysisSessions;
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly Core.Services.DocumentOpenService _documentOpenService;
        private readonly Core.Services.DeadlockDocumentController _deadlockDocumentController;
        private readonly Core.Services.PlanDocumentController _planDocumentController;
        private readonly DeadlockAnalysisUiActionService _deadlockAnalysisUiActionService;
        private readonly DeadlockPlaybackUiActionService _deadlockPlaybackUiActionService;
        private readonly PlanAnalysisUiActionService _planAnalysisUiActionService;
        private readonly SqlDiffUiActionService _sqlDiffUiActionService;
        private readonly SqlQuickFixUiActionService _sqlQuickFixUiActionService;
        private readonly PlanStatisticsUiActionService _planStatisticsUiActionService;
        private readonly TextBlock _statusTextBlock;
        private readonly XNamespace _showplanNamespace;
        private readonly Action _updatePlaybackGraphVisibility;
        private readonly Action<InputRecognitionResult, string> _showDeadlockEvents;
        private readonly Action _clearDeadlockEvents;

        public DocumentAnalysisUiActionService(
            Core.Services.AnalysisSessionCoordinator analysisSessions,
            Core.ViewModels.MainViewModel viewModel,
            Core.Services.DocumentOpenService documentOpenService,
            Core.Services.DeadlockDocumentController deadlockDocumentController,
            Core.Services.PlanDocumentController planDocumentController,
            DeadlockAnalysisUiActionService deadlockAnalysisUiActionService,
            DeadlockPlaybackUiActionService deadlockPlaybackUiActionService,
            PlanAnalysisUiActionService planAnalysisUiActionService,
            SqlDiffUiActionService sqlDiffUiActionService,
            SqlQuickFixUiActionService sqlQuickFixUiActionService,
            PlanStatisticsUiActionService planStatisticsUiActionService,
            TextBlock statusTextBlock,
            XNamespace showplanNamespace,
            Action updatePlaybackGraphVisibility,
            Action<InputRecognitionResult, string> showDeadlockEvents,
            Action clearDeadlockEvents)
        {
            _analysisSessions = analysisSessions
                ?? throw new ArgumentNullException(nameof(analysisSessions));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _documentOpenService = documentOpenService
                ?? throw new ArgumentNullException(nameof(documentOpenService));
            _deadlockDocumentController = deadlockDocumentController
                ?? throw new ArgumentNullException(nameof(deadlockDocumentController));
            _planDocumentController = planDocumentController
                ?? throw new ArgumentNullException(nameof(planDocumentController));
            _deadlockAnalysisUiActionService = deadlockAnalysisUiActionService
                ?? throw new ArgumentNullException(nameof(deadlockAnalysisUiActionService));
            _deadlockPlaybackUiActionService = deadlockPlaybackUiActionService
                ?? throw new ArgumentNullException(nameof(deadlockPlaybackUiActionService));
            _planAnalysisUiActionService = planAnalysisUiActionService
                ?? throw new ArgumentNullException(nameof(planAnalysisUiActionService));
            _sqlDiffUiActionService = sqlDiffUiActionService
                ?? throw new ArgumentNullException(nameof(sqlDiffUiActionService));
            _sqlQuickFixUiActionService = sqlQuickFixUiActionService
                ?? throw new ArgumentNullException(nameof(sqlQuickFixUiActionService));
            _planStatisticsUiActionService = planStatisticsUiActionService
                ?? throw new ArgumentNullException(nameof(planStatisticsUiActionService));
            _statusTextBlock = statusTextBlock
                ?? throw new ArgumentNullException(nameof(statusTextBlock));
            _showplanNamespace = showplanNamespace
                ?? throw new ArgumentNullException(nameof(showplanNamespace));
            _updatePlaybackGraphVisibility = updatePlaybackGraphVisibility
                ?? throw new ArgumentNullException(nameof(updatePlaybackGraphVisibility));
            _showDeadlockEvents = showDeadlockEvents ?? throw new ArgumentNullException(nameof(showDeadlockEvents));
            _clearDeadlockEvents = clearDeadlockEvents ?? throw new ArgumentNullException(nameof(clearDeadlockEvents));
        }

        public async Task AnalyzeFileAsync(string filePath)
        {
            Core.Services.AnalysisSession session = _analysisSessions.Begin();
            _clearDeadlockEvents();
            try
            {
                _statusTextBlock.Text = $"Loading and identifying file: {System.IO.Path.GetFileName(filePath)}...";
                Core.Services.DocumentOpenResult openResult =
                    await _documentOpenService.OpenAsync(filePath, session.Token);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                if (openResult.Status == InputStatus.Cancelled)
                {
                    _statusTextBlock.Text = "读取已取消";
                    return;
                }
                if (!openResult.HasUsableContent)
                {
                    Logger.Error($"Document open failed: {filePath}. {openResult.ErrorMessage}");
                    MessageBox.Show($"{openResult.ErrorCode}: {openResult.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _statusTextBlock.Text = "File load failed";
                    return;
                }

                if (openResult.Status == InputStatus.Partial)
                {
                    MessageBox.Show(InputReadPresentation.Describe(openResult.Input!), "部分读取", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                if (openResult.Kind is AnalysisDocumentKind.DeadlockXml or AnalysisDocumentKind.XelDeadlockTrace)
                {
                    _viewModel.CurrentDeadlockInput = openResult.Input;
                    _viewModel.CurrentDeadlockFilePath = filePath;
                    if (openResult.Deadlocks.Count > 1 || openResult.Status == InputStatus.Partial ||
                        openResult.Kind == AnalysisDocumentKind.XelDeadlockTrace)
                    {
                        _statusTextBlock.Text = $"{openResult.Status}：已读取 {openResult.Deadlocks.Count} 个死锁事件。";
                        _showDeadlockEvents(openResult.Input!, filePath);
                        return;
                    }
                    await AnalyzeDeadlockDocumentAsync(openResult.Deadlocks[0].Document, filePath, session.RequestId, session.Token);
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
                    _statusTextBlock.Text = "File load failed";
                    return;
                }

                if (openResult.Kind == Core.Services.AnalysisDocumentKind.ExecutionPlanXml)
                {
                    _viewModel.CurrentPlanInput = openResult.Input;
                    Logger.Info($"File identified as a SQL Server execution plan: {filePath}");
                    _viewModel.CurrentPlanFilePath = filePath;
                    await AnalyzeExecutionPlanDocumentAsync(doc, filePath, session.RequestId, session.Token);
                }
                else
                {
                    Logger.Warning($"File format could not be identified: {filePath}. Root LocalName: {doc.Root?.Name.LocalName}, Namespace: {doc.Root?.Name.Namespace.NamespaceName}");
                    MessageBox.Show("The XML file type could not be identified. Please choose a SQL Server deadlock XML file or execution plan XML file.", "Unrecognized format", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _statusTextBlock.Text = "Unknown file type";
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"File analysis was canceled: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                Logger.LogException("AnalyzeFile", ex);
                MessageBox.Show($"File analysis failed: {ex.Message}\n\nDetails were written to the log.", "Analysis error", MessageBoxButton.OK, MessageBoxImage.Error);
                _statusTextBlock.Text = "Analysis failed";
            }
        }

        public async Task AnalyzeDeadlockXmlAsync(string xml, string sourceFilePath, string displayName)
        {
            Core.Services.AnalysisSession session = _analysisSessions.Begin();
            try
            {
                _statusTextBlock.Text = $"Analyzing: {displayName}...";
                InputRecognitionResult input = await Task.Run(
                    () => new InputRecognitionService().Parse(xml, session.Token),
                    session.Token);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                if (!input.IsSuccess || input.Kind != AnalysisDocumentKind.DeadlockXml)
                {
                    MessageBox.Show($"{input.ErrorCode ?? "INPUT_EXPECTED_DEADLOCK"}: {input.ErrorMessage ?? "需要死锁 XML 文档。"}",
                        "输入无法分析", MessageBoxButton.OK, MessageBoxImage.Error);
                    _statusTextBlock.Text = "Deadlock input rejected";
                    return;
                }
                if (input.Deadlocks.Count > 1)
                {
                    _showDeadlockEvents(input, sourceFilePath);
                    return;
                }
                _viewModel.CurrentDeadlockFilePath = sourceFilePath;
                await AnalyzeDeadlockDocumentAsync(input.Deadlocks[0].Document, sourceFilePath, session.RequestId, session.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"In-memory deadlock analysis was canceled: {displayName}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                Logger.LogException("AnalyzeDeadlockXmlAsync", ex);
                MessageBox.Show($"Deadlock analysis failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _statusTextBlock.Text = "Analysis failed";
            }
        }

        public async Task AnalyzeDeadlockDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken)
        {
            try
            {
                _statusTextBlock.Text = $"Analyzing deadlock file: {System.IO.Path.GetFileName(filePath)}...";

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
                _updatePlaybackGraphVisibility();
                _statusTextBlock.Text = uiResult.StatusText;
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"Deadlock analysis was canceled: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }

                Logger.LogException("AnalyzeDeadlockDocument", ex);
                MessageBox.Show($"Deadlock analysis failed: {ex.Message}\n\nDetails were written to the log.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _statusTextBlock.Text = "Analysis failed";
            }
        }

        public async Task AnalyzeExecutionPlanDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken)
        {
            try
            {
                _statusTextBlock.Text = $"Analyzing execution plan: {System.IO.Path.GetFileName(filePath)}...";

                Core.Services.PlanDocumentResult documentResult =
                    await _planDocumentController.AnalyzeAsync(
                        doc,
                        filePath,
                        _showplanNamespace,
                        cancellationToken);
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }

                Core.Services.PlanAnalysisOutput result = documentResult.Analysis;
                Logger.Info($"[ExecutionPlan] Mermaid length: {result.Mermaid.Length} characters");
                PlanAnalysisUiResult uiResult =
                    _planAnalysisUiActionService.Apply(documentResult);
                _sqlDiffUiActionService.SetSql(
                    uiResult.QueryText,
                    uiResult.RefactoredSql,
                    _sqlQuickFixUiActionService.CreateLightbulbButton);
                try
                {
                    _planStatisticsUiActionService.LoadFromPlan(doc, _showplanNamespace);
                }
                catch (Exception ex)
                {
                    Logger.LogException("Load Histogram", ex);
                }

                _statusTextBlock.Text = "Execution plan analysis complete";
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"Execution plan analysis was canceled: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }

                Logger.LogException("AnalyzeExecutionPlanDocument", ex);
                MessageBox.Show($"Execution plan analysis failed: {ex.Message}\n\nDetails were written to the log.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _statusTextBlock.Text = "Analysis failed";
            }
        }
    }
}
