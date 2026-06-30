using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DocumentAnalysisUiActionService
    {
        private readonly Core.Services.AnalysisSessionCoordinator _analysisSessions;
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

        public DocumentAnalysisUiActionService(
            Core.Services.AnalysisSessionCoordinator analysisSessions,
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
            Action updatePlaybackGraphVisibility)
        {
            _analysisSessions = analysisSessions
                ?? throw new ArgumentNullException(nameof(analysisSessions));
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
        }

        public async Task AnalyzeDeadlockDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken)
        {
            try
            {
                _statusTextBlock.Text = $"正在分析死锁文件：{System.IO.Path.GetFileName(filePath)}...";

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
                _statusTextBlock.Text = "分析失败";
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
                _statusTextBlock.Text = $"正在分析执行计划：{System.IO.Path.GetFileName(filePath)}...";

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

                _statusTextBlock.Text = "执行计划分析完成";
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
                _statusTextBlock.Text = "分析失败";
            }
        }
    }
}
