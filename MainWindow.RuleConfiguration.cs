using System.IO;
using System.Windows;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Views;

namespace SqlXmlAnalyzer;

public partial class MainWindow
{
    private async void ReanalyzePlanFromWorkspace()
    {
        try { await RecalculateCurrentPlanAsync(CancellationToken.None); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ShellStatus.StatusTextBlock.Text = ExceptionPolicy.Describe(exception, "PlanWorkspace.Reanalyze");
        }
    }

    private async void OpenRuleConfiguration(object sender, RoutedEventArgs args)
    {
        try
        {
            if (_ruleConfigurationWindow != null) { _ruleConfigurationWindow.Activate(); return; }
            var model = new RuleConfigurationViewModel(_ruleConfiguration, ApplyRuleConfiguration,
                RecalculateCurrentPlanAsync, () => ViewModel.CurrentPlanSource != null);
            var window = new RuleConfigurationWindow(model) { Owner = this };
            _ruleConfigurationWindow = window;
            window.Closed += (_, _) => _ruleConfigurationWindow = null;
            window.Show();
            await model.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, ExceptionPolicy.Describe(exception, "IMP24.Configuration.OpenWindow"), "规则配置打开失败");
        }
    }

    private void ApplyRuleConfiguration(bool changed)
    {
        if (changed)
        {
            if (_analysisSessions.CancelCurrent(Core.Services.AnalysisDocumentKind.ExecutionPlanXml))
                Logger.Debug("IMP24 configuration changed; pending execution-plan analysis cancelled.");
            ViewModel.CurrentRewriteReview = null;
        }
        ViewModel.PlanWorkspace.SetActiveConfiguration(_ruleConfiguration.Capture().Fingerprint);
        if (ViewModel.PlanWorkspace.Report != null) ViewModel.PlanWarningsText = ViewModel.PlanWorkspace.SelectedReportText;
    }

    private async Task RecalculateCurrentPlanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = ViewModel.CurrentPlanSource ?? throw new InvalidDataException("请先打开并完成执行计划分析。");
        var previousReport = ViewModel.PlanWorkspace.Report;
        var session = _analysisSessions.Begin(Core.Services.AnalysisDocumentKind.ExecutionPlanXml);
        using var registration = cancellationToken.Register(() => _analysisSessions.Cancel(session.RequestId));
        await _documentAnalysisUiActionService.AnalyzeExecutionPlanDocumentAsync(source.Document,
            source.FilePath, session.RequestId, session.Token, source.Input);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_analysisSessions.IsCurrent(session.RequestId) || ReferenceEquals(previousReport, ViewModel.PlanWorkspace.Report)
            || ViewModel.PlanWorkspace.NeedsConfigurationReanalysis)
            throw new InvalidDataException("重新分析未完成；请查看工作区状态，原结果保留。");
    }
}
