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
        private readonly Func<Task<bool>> _waitForPlanView;
        private readonly Func<long, IDisposable>? _beginPlanView;

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
            Action clearDeadlockEvents,
            Func<Task<bool>>? waitForPlanView = null,
            Func<long, IDisposable>? beginPlanView = null)
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
            _waitForPlanView = waitForPlanView ?? (() => Task.FromResult(true));
            _beginPlanView = beginPlanView;
        }

        public async Task AnalyzeFileAsync(string filePath)
        {
            Core.Services.AnalysisSession session = _analysisSessions.Begin();
            try
            {
                _clearDeadlockEvents();
                _statusTextBlock.Text = $"Loading and identifying file: {System.IO.Path.GetFileName(filePath)}...";
                Core.Services.DocumentOpenResult openResult =
                    await _documentOpenService.OpenAsync(filePath, session.Token, state => _analysisSessions.Report(session.RequestId, state));
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                if (openResult.Status == InputStatus.Cancelled)
                {
                    _analysisSessions.Cancel(session.RequestId);
                    _statusTextBlock.Text = "读取已取消";
                    return;
                }
                if (!openResult.HasUsableContent)
                {
                    _analysisSessions.Report(session.RequestId, AnalysisOperationState.Failed, openResult.ErrorMessage ?? openResult.ErrorCode ?? "输入不可用");
                    Logger.Error($"IMP26 document open failed: {openResult.ErrorCode}.");
                    _statusTextBlock.Text = $"{openResult.ErrorCode}: {openResult.ErrorMessage}";
                    return;
                }

                if (openResult.Kind is AnalysisDocumentKind.DeadlockXml or AnalysisDocumentKind.XelDeadlockTrace)
                {
                    _viewModel.CurrentDeadlockInput = openResult.Input;
                    _viewModel.CurrentDeadlockFilePath = filePath;
                    _statusTextBlock.Text = $"{openResult.Status}：已读取 {openResult.Deadlocks.Count} 个死锁事件。";
                    _showDeadlockEvents(openResult.Input!, filePath);
                    if (!_analysisSessions.TrySetKind(session.RequestId, openResult.Kind)) return;
                    if (openResult.Deadlocks.Count == 0)
                    {
                        _analysisSessions.Report(session.RequestId, AnalysisOperationState.Failed, "未采集可分析的死锁事件。");
                        return;
                    }
                    _deadlockPlaybackUiActionService.HidePlayback();
                    _deadlockPlaybackUiActionService.SetCurrentPlayback(null, null);
                    _viewModel.DeadlockWorkspace.Begin(openResult.Input, openResult.Deadlocks[0]);
                    await AnalyzeDeadlockDocumentAsync(openResult.Deadlocks[0].Document, filePath, session.RequestId, session.Token);
                    return;
                }

                XDocument? doc = openResult.Document;
                if (doc == null)
                {
                    _analysisSessions.Report(session.RequestId, AnalysisOperationState.Failed, "输入未产生 XML 文档。");
                    _statusTextBlock.Text = "File load failed";
                    return;
                }

                if (openResult.Kind == Core.Services.AnalysisDocumentKind.ExecutionPlanXml)
                {
                    Logger.Info($"File identified as a SQL Server execution plan: {filePath}");
                    await AnalyzeExecutionPlanDocumentAsync(doc, filePath, session.RequestId, session.Token, openResult.Input);
                }
                else
                {
                    Logger.Warning($"File format could not be identified: {filePath}. Root LocalName: {doc.Root?.Name.LocalName}, Namespace: {doc.Root?.Name.Namespace.NamespaceName}");
                    _analysisSessions.Report(session.RequestId, AnalysisOperationState.Failed, "无法识别输入类型。");
                    _statusTextBlock.Text = "Unknown file type";
                }
            }
            catch (OperationCanceledException)
            {
                _analysisSessions.Cancel(session.RequestId);
                Logger.Verbose($"File analysis was canceled: {filePath}");
            }
            catch (Exception ex)
            {
                _analysisSessions.Fail(session.RequestId, ex);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                Logger.LogException("AnalyzeFile", ex);
                _statusTextBlock.Text = _analysisSessions.Progress.Detail;
            }
        }

        public async Task AnalyzeDeadlockXmlAsync(string xml, string sourceFilePath, string displayName)
        {
            Core.Services.AnalysisSession session = _analysisSessions.Begin(AnalysisDocumentKind.DeadlockXml);
            try
            {
                _statusTextBlock.Text = $"Analyzing: {displayName}...";
                _analysisSessions.Report(session.RequestId, AnalysisOperationState.Parsing);
                InputRecognitionResult input = await Task.Run(
                    () => new InputRecognitionService().Parse(xml, session.Token),
                    session.Token);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                if (!input.IsSuccess || input.Kind != AnalysisDocumentKind.DeadlockXml)
                {
                    _analysisSessions.Report(session.RequestId, AnalysisOperationState.Failed, input.ErrorMessage ?? "需要死锁 XML 文档。");
                    MessageBox.Show($"{input.ErrorCode ?? "INPUT_EXPECTED_DEADLOCK"}: {input.ErrorMessage ?? "需要死锁 XML 文档。"}",
                        "输入无法分析", MessageBoxButton.OK, MessageBoxImage.Error);
                    _statusTextBlock.Text = "Deadlock input rejected";
                    return;
                }
                if (input.Deadlocks.Count > 1)
                {
                    _showDeadlockEvents(input, sourceFilePath);
                }
                _viewModel.CurrentDeadlockInput = input;
                _viewModel.CurrentDeadlockFilePath = sourceFilePath;
                _viewModel.DeadlockWorkspace.Begin(input, input.Deadlocks[0]);
                await AnalyzeDeadlockDocumentAsync(input.Deadlocks[0].Document, sourceFilePath, session.RequestId, session.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"In-memory deadlock analysis was canceled: {displayName}");
                _analysisSessions.Cancel(session.RequestId);
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                Logger.LogException("AnalyzeDeadlockXmlAsync", ex);
                _analysisSessions.Fail(session.RequestId, ex);
                MessageBox.Show($"Deadlock analysis failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _statusTextBlock.Text = "Analysis failed";
            }
        }

        public async Task SelectDeadlockEventAsync(DeadlockInput selected, InputRecognitionResult? input, string source)
        {
            var session = _analysisSessions.Begin(input?.Kind ?? AnalysisDocumentKind.DeadlockXml);
            try
            {
                _deadlockPlaybackUiActionService.HidePlayback();
                _deadlockPlaybackUiActionService.SetCurrentPlayback(null, null);
                _viewModel.DeadlockWorkspace.Begin(input, selected);
                if (!ReferenceEquals(_viewModel.DeadlockWorkspace.SelectedEvent, selected))
                {
                    _analysisSessions.Report(session.RequestId, AnalysisOperationState.Failed, "事件不在当前输入快照中。");
                    return;
                }
                _viewModel.CurrentDeadlockInput = input;
                _viewModel.CurrentDeadlockFilePath = source;
                await AnalyzeDeadlockDocumentAsync(selected.Document, source, session.RequestId, session.Token);
            }
            catch (OperationCanceledException) { _analysisSessions.Cancel(session.RequestId); }
            catch (Exception exception)
            {
                string detail = _analysisSessions.Fail(session.RequestId, exception);
                if (_analysisSessions.IsCurrent(session.RequestId)) _statusTextBlock.Text = detail;
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
                _analysisSessions.Report(requestId, AnalysisOperationState.Analyzing);

                Core.Services.DeadlockDocumentResult documentResult =
                    await _deadlockDocumentController.AnalyzeAsync(
                        doc,
                        filePath,
                        cancellationToken);
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }

                _analysisSessions.Report(requestId, AnalysisOperationState.PreparingView);
                if (!_analysisSessions.IsCurrent(requestId)) return;
                DeadlockAnalysisUiResult uiResult =
                    await _deadlockAnalysisUiActionService.ApplyAsync(documentResult, cancellationToken);
                if (!_analysisSessions.IsCurrent(requestId)) return;
                _deadlockPlaybackUiActionService.SetCurrentPlayback(
                    uiResult.Timeline,
                    uiResult.PlaybackViewModel);
                _updatePlaybackGraphVisibility();
                _statusTextBlock.Text = uiResult.StatusText;
                _analysisSessions.Report(requestId, _viewModel.CurrentDeadlockInput?.Status == InputStatus.Partial
                    ? AnalysisOperationState.Partial : AnalysisOperationState.Ready,
                    _viewModel.CurrentDeadlockInput?.Status == InputStatus.Partial ? InputReadPresentation.Describe(_viewModel.CurrentDeadlockInput) : "");
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"Deadlock analysis was canceled: {filePath}");
                _analysisSessions.Cancel(requestId);
            }
            catch (Exception ex)
            {
                _analysisSessions.Fail(requestId, ex);
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }

                _viewModel.DeadlockWorkspace.ReportFailure(ex);
                _statusTextBlock.Text = _viewModel.DeadlockWorkspace.Status;
            }
        }

        public async Task AnalyzeExecutionPlanDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken)
            => await AnalyzeExecutionPlanDocumentAsync(doc, filePath, requestId, cancellationToken, null);

        public async Task AnalyzeExecutionPlanDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken,
            InputRecognitionResult? input)
        {
            try
            {
                if (!_analysisSessions.TrySetKind(requestId, AnalysisDocumentKind.ExecutionPlanXml)) return;
                _analysisSessions.Report(requestId, AnalysisOperationState.Analyzing);
                _statusTextBlock.Text = $"Analyzing execution plan: {System.IO.Path.GetFileName(filePath)}...";

                Core.Services.PlanDocumentResult documentResult =
                    await _planDocumentController.AnalyzeAsync(
                        doc,
                        filePath,
                        _showplanNamespace,
                        cancellationToken, input);
                if (!_analysisSessions.IsCurrent(requestId))
                {
                    return;
                }

                Core.Services.PlanAnalysisOutput result = documentResult.Analysis;
                _analysisSessions.Report(requestId, AnalysisOperationState.PreparingView);
                if (!_analysisSessions.IsCurrent(requestId)) return;
                Logger.Info($"[ExecutionPlan] Mermaid length: {result.Mermaid.Length} characters");
                using var viewOperation = _beginPlanView?.Invoke(requestId);
                PlanAnalysisUiResult uiResult =
                    _planAnalysisUiActionService.Apply(documentResult);
                if (!await _waitForPlanView() || !_analysisSessions.IsCurrent(requestId)) return;
                _sqlDiffUiActionService.SetSql(
                    _viewModel.PlanWorkspace.Selection?.Choice.Statement.Text ?? uiResult.QueryText,
                    _viewModel.PlanWorkspace.Model?.Statements.Count > 1
                        ? _viewModel.PlanWorkspace.Selection?.Choice.Statement.Text ?? "" : uiResult.RefactoredSql,
                    _sqlQuickFixUiActionService.CreateLightbulbButton);
                bool presentationFailed = false;
                try
                {
                    var choice = _viewModel.PlanWorkspace.Selection?.Choice;
                    var source = choice?.QueryPlan == null ? null : _viewModel.PlanWorkspace.Model!.GetQueryPlanSource(choice.QueryPlan.Key);
                    _planStatisticsUiActionService.LoadFromPlan(source == null ? new XDocument() : new XDocument(new XElement(source)), _showplanNamespace);
                }
                catch (Exception ex)
                {
                    Core.Diagnostics.ExceptionPolicy.Describe(ex, "Load Histogram");
                    presentationFailed = true;
                }

                _statusTextBlock.Text = "Execution plan analysis complete";
                bool partial = presentationFailed || result.RefactoringFailed || input?.Status == InputStatus.Partial || result.Diagnostics?.HasFailures == true;
                _analysisSessions.Report(requestId, partial ? AnalysisOperationState.Partial : AnalysisOperationState.Ready,
                    partial ? "输入或部分分析阶段未完成；请查看诊断、规则运行状态及改写说明。" : "");
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"Execution plan analysis was canceled: {filePath}");
                _analysisSessions.Cancel(requestId);
            }
            catch (Exception ex)
            {
                string detail = _analysisSessions.Fail(requestId, ex);
                if (!_analysisSessions.IsCurrent(requestId)) return;
                _statusTextBlock.Text = detail;
            }
        }
    }
}
