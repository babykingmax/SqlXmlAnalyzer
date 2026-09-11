using System;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Services;

internal sealed record PlanAnalysisUiResult(string QueryText, string RefactoredSql);

internal sealed class PlanAnalysisUiActionService
{
    private readonly Core.ViewModels.MainViewModel _viewModel;
    private readonly TabControl _mainTabControl;
    private readonly TabControl _planGraphTabControl;

    public PlanAnalysisUiActionService(Core.ViewModels.MainViewModel viewModel,
        TabControl mainTabControl, TabControl planGraphTabControl)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _mainTabControl = mainTabControl ?? throw new ArgumentNullException(nameof(mainTabControl));
        _planGraphTabControl = planGraphTabControl ?? throw new ArgumentNullException(nameof(planGraphTabControl));
    }

    public PlanAnalysisUiResult Apply(Core.Services.PlanDocumentResult documentResult)
    {
        ArgumentNullException.ThrowIfNull(documentResult);
        var analysis = documentResult.Analysis;
        var source = new Core.Services.PlanAnalysisSource(documentResult.Document, documentResult.FilePath, documentResult.Input);
        _viewModel.ActivateWorkspace(Core.ViewModels.WorkspaceMode.ExecutionPlan);
        _mainTabControl.SelectedIndex = 1;
        // Preserve the user's graph/hotspot view when another plan is opened.
        if (_planGraphTabControl.SelectedIndex < 0) _planGraphTabControl.SelectedIndex = 0;
        _viewModel.PlanWorkspace.Open(documentResult.Document, analysis.Diagnostics, analysis.MissingIndexes,
            source.Input, analysis.RefactoringNotices);
        if (_viewModel.PlanWorkspace.Model == null)
            throw new System.IO.InvalidDataException(_viewModel.PlanWorkspace.Status);
        _viewModel.CommitPlanSource(source);
        _viewModel.CurrentPlanDiagnostics = analysis.Diagnostics;
        _viewModel.CurrentRewriteReview = _viewModel.PlanWorkspace.Model?.Statements.Count == 1 ? analysis.RewriteReview : null;
        _viewModel.CurrentRewriteSource = _viewModel.CurrentRewriteReview == null ? "" : analysis.QueryText;
        return new(analysis.QueryText, analysis.RefactoredSql);
    }
}
