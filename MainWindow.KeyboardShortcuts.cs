using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer;

public partial class MainWindow
{
    private readonly WorkspaceInteractionService _interaction = new();
    private bool CanReport => MainTabControl.SelectedIndex == 0 ? ViewModel.DeadlockWorkspace.Analysis != null
        : MainTabControl.SelectedIndex == 1 && ViewModel.PlanWorkspace.Report != null;

    private void WireKeyboardShortcuts()
    {
        Bind(WorkspaceCommands.OpenPlan, () => OpenPlanFile_Click(this, new()));
        Bind(WorkspaceCommands.OpenDeadlock, () => OpenDeadlockFile_Click(this, new()));
        Bind(WorkspaceCommands.ExportHtml, () => GenerateHtmlReport_Click(this, new()), () => CanReport);
        Bind(WorkspaceCommands.ExportWord, () => ExportToWord_Click(this, new()), () => CanReport);
        Bind(WorkspaceCommands.ExportPdf, () => ExportToPdf_Click(this, new()), () => CanReport);
        Bind(WorkspaceCommands.ExportPlan, () => ExportObfuscatedPlan_Click(this, new()), () => ViewModel.CurrentPlanSource != null);
        Bind(WorkspaceCommands.CopyResults, () => CopyAnalysisResult_Click(this, new()), () => CanReport);
        Bind(WorkspaceCommands.Clear, () => ClearResults_Click(this, new()), () => ViewModel.CurrentPlanDoc != null || ViewModel.CurrentDeadlockDoc != null);
        Bind(WorkspaceCommands.About, () => About_Click(this, new()));
        Bind(WorkspaceCommands.Help, () => new Views.InteractionHelpWindow(ViewModel) { Owner = this }.ShowDialog());
        Bind(WorkspaceCommands.Configuration, () => OpenRuleConfiguration(this, new()));
        Bind(WorkspaceCommands.ReanalyzePlan, ReanalyzePlanFromWorkspace,
            () => ViewModel.CurrentPlanSource != null && !_analysisSessions.Progress.IsBusy);
        Bind(WorkspaceCommands.DiagnosticPackage, ShowDiagnosticPackage);
        Bind(WorkspaceCommands.NextIssue, () => MoveIssue(1), () => MainTabControl.SelectedIndex == 1 && ViewModel.PlanWorkspace.FilteredFindings.Count > 0);
        Bind(WorkspaceCommands.PreviousIssue, () => MoveIssue(-1), () => MainTabControl.SelectedIndex == 1 && ViewModel.PlanWorkspace.FilteredFindings.Count > 0);
        Bind(WorkspaceCommands.Evidence, () => PlanWorkspace.OpenEvidence(), () => MainTabControl.SelectedIndex == 1 && ViewModel.PlanWorkspace.SourceTarget != null);
        Bind(WorkspaceCommands.Graph, () => PlanWorkspace.FocusGraph(), () => MainTabControl.SelectedIndex == 1 && PlanWorkspace.NodifyGraph.Nodes.Count > 0);
        Bind(WorkspaceCommands.Compare, () => { MainTabControl.SelectedIndex = 2; WorkspaceAccessibility.Focus(PlanComparisonWorkspace.TuningHistoryList); });
        Bind(WorkspaceCommands.Plan, () => { MainTabControl.SelectedIndex = 1; PlanWorkspace.FocusStatement(); });
        Bind(WorkspaceCommands.Deadlock, () => { MainTabControl.SelectedIndex = 0; DeadlockWorkspace.MoveWorkspaceFocus(false); });
        ViewModel.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
        ViewModel.PlanWorkspace.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
        MainTabControl.SelectionChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F6 || (Keyboard.Modifiers & ~ModifierKeys.Shift) != 0) return;
            _interaction.Run("FocusCycle", () => true, () =>
            {
                bool reverse = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (MainTabControl.SelectedIndex == 1) PlanWorkspace.MoveWorkspaceFocus(reverse);
                else if (MainTabControl.SelectedIndex == 2) PlanComparisonWorkspace.MoveWorkspaceFocus(reverse);
                else DeadlockWorkspace.MoveWorkspaceFocus(reverse);
            }, detail => ShellStatus.StatusTextBlock.Text = detail);
            e.Handled = true;
        };
    }

    private void Bind(RoutedUICommand command, Action action, Func<bool>? canExecute = null)
    {
        canExecute ??= () => true;
        var predicate = canExecute;
        CommandBindings.Add(new CommandBinding(command,
            (_, e) => { _interaction.Run(command.Name, predicate, action, detail => ShellStatus.StatusTextBlock.Text = detail); e.Handled = true; },
            (_, e) => { e.CanExecute = predicate(); e.Handled = true; }));
    }

    private void MoveIssue(int direction)
    {
        var workspace = ViewModel.PlanWorkspace;
        var findings = workspace.FilteredFindings;
        if (findings.Count == 0) return;
        int current = findings.ToList().IndexOf(workspace.SelectedIssue!);
        workspace.SelectedIssue = findings[WorkspaceInteractionService.MoveSelection(findings.Count, current, direction)];
        PlanWorkspace.FocusIssues();
    }
}
