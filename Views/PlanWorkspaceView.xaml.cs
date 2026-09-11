using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views;

public partial class PlanWorkspaceView : UserControl
{
    private IInputElement? _evidenceOrigin;
    private PlanNodeViewModel? _detailsOriginNode;
    private bool _auxiliaryWasOpen;

    public PlanWorkspaceView()
    {
        InitializeComponent();
        InitializePresentation();
        PreviewKeyDown += Workspace_PreviewKeyDown;
    }

    public Grid ContentGrid => PlanContentGrid;
    public TreeView OperatorTree => PlanOperatorTree;
    public TreeView VisualTree => PlanVisualTree;
    public PlanGraphControl NodifyGraph => PlanNodifyGraph;
    public DataGrid PropertiesGrid => PlanPropertiesGrid;
    public DataGrid RecostDataGrid => RecostGrid;
    public TextBox XmlTextBox => PlanXmlTextBox;
    public TextBox StatementTextBox => PlanStatementTextBox;
    public TextBox WarningsTextBox => PlanWarningsTextBox;
    public TabControl GraphTabControl => PlanGraphTabControl;
    public StatisticsHistogramControl StatisticsHistogram => StatisticsHistogramView;
    public RichTextBox OriginalSqlText => OriginalSqlTextBox;
    public RichTextBox RefactoredSqlText => RefactoredSqlTextBox;
    public ColumnDefinition OriginalSqlColumn => OriginalSqlCol;
    public ColumnDefinition SqlSplitterColumn => SqlSplitterCol;
    public GridSplitter SqlSplitter => SqlGridSplitter;
    public Button CompareSqlButton => BtnCompareSql;
    public TabControl AuxiliaryTabControl => AuxiliaryTabs;

    public event RoutedEventHandler? LeftPanelExpanded;
    public event RoutedEventHandler? LeftPanelCollapsed;
    public event RoutedEventHandler? RightPanelExpanded;
    public event RoutedEventHandler? RightPanelCollapsed;
    public event RoutedPropertyChangedEventHandler<object>? OperatorTreeSelectedItemChanged;
    public event RoutedPropertyChangedEventHandler<object>? VisualTreeSelectedItemChanged;
    public event EventHandler<PlanNodeViewModel?>? GraphNodeSelected;
    public event EventHandler<PlanNodeViewModel?>? GraphNodeDoubleClicked;
    public event RoutedEventHandler? CopyRefactoredSqlClicked;
    public event RoutedEventHandler? CompareSqlClicked;
    public event RoutedEventHandler? CopyIndexDdlClicked;
    public event RoutedEventHandler? CopyDeploymentBundleClicked;
    public event RoutedEventHandler? CopyRollbackDdlClicked;

    public void OpenEvidence()
    {
        if (!EvidenceSourceTab.IsSelected || !AuxiliaryPanel.IsExpanded || _evidenceOrigin == null)
        {
            _evidenceOrigin = Keyboard.FocusedElement;
            _auxiliaryWasOpen = AuxiliaryPanel.IsExpanded;
        }
        OpenAuxiliary("evidence");
        Services.WorkspaceAccessibility.Focus(EvidenceSql);
    }

    public void OpenAuxiliary(string section)
    {
        AuxiliaryPanel.IsExpanded = true;
        (section switch
        {
            "xml" => XmlSourceTab,
            "evidence" => EvidenceSourceTab,
            "statistics" => StatisticsTab,
            "rewrite" => SqlRefactorTab,
            _ => StatementSourceTab
        }).IsSelected = true;
        QueueSavePreferences();
    }

    public void FocusStatement() => Services.WorkspaceAccessibility.Focus(StatementSelector);
    public void FocusGraph()
    {
        ShowGraph();
        PlanNodifyGraph.FocusFirstNode();
    }
    public void ShowGraph()
    {
        _activePane = PlanWorkspacePane.Graph;
        GraphTab.IsSelected = true;
        UpdateLayoutMode();
        QueueSavePreferences();
    }
    public void FocusIssues()
    {
        _leftOpen = true;
        _activePane = PlanWorkspacePane.Issues;
        FindingsTab.IsSelected = true;
        UpdateLayoutMode();
        if (DiagnosticIssuesGrid.SelectedItem != null) DiagnosticIssuesGrid.ScrollIntoView(DiagnosticIssuesGrid.SelectedItem);
        Services.WorkspaceAccessibility.Focus(DiagnosticIssuesGrid);
        QueueSavePreferences();
    }
    public void MoveWorkspaceFocus(bool reverse)
    {
        FrameworkElement[] groups = [StatementSelector, PlanNodifyGraph, DiagnosticIssuesGrid, DiagnosticEvidenceSelector];
        int current = Array.FindIndex(groups, element => element.IsKeyboardFocusWithin);
        int next = WorkspaceInteractionService.MoveSelection(groups.Length, current, reverse ? -1 : 1);
        if (next == 1) FocusGraph();
        else if (next == 2) FocusIssues();
        else if (next == 3)
        {
            ShowDetails();
            Services.WorkspaceAccessibility.Focus(DiagnosticEvidenceSelector);
        }
        else FocusStatement();
    }

    private void Workspace_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Services.WorkspaceAccessibility.Descendants(this).OfType<ComboBox>().Any(combo => combo.IsDropDownOpen)) return;
        if (AuxiliaryPanel.IsKeyboardFocusWithin && _evidenceOrigin != null)
        {
            AuxiliaryPanel.IsExpanded = _auxiliaryWasOpen;
            if (_evidenceOrigin is FrameworkElement origin && origin.IsVisible) Services.WorkspaceAccessibility.Focus(origin);
            else FocusIssues();
            _evidenceOrigin = null;
            e.Handled = true;
        }
        else if (_detailsOriginNode != null && RightPanel.IsKeyboardFocusWithin)
        {
            if (PlanNodifyGraph.AllNodes.Contains(_detailsOriginNode)) PlanNodifyGraph.SelectKeyboardNode(_detailsOriginNode);
            ShowGraph();
            Services.WorkspaceAccessibility.Focus(PlanNodifyGraph);
            _detailsOriginNode = null;
            e.Handled = true;
        }
    }

    private void ShowEvidenceSource_Click(object sender, RoutedEventArgs e) => OpenEvidence();
    private void PlanOperatorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => OperatorTreeSelectedItemChanged?.Invoke(sender, e);
    private void PlanVisualTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => VisualTreeSelectedItemChanged?.Invoke(sender, e);
    private void PlanNodifyGraph_NodeSelected(object? sender, PlanNodeViewModel? node) => GraphNodeSelected?.Invoke(sender ?? this, node);

    private void PlanNodifyGraph_NodeDoubleClicked(object? sender, PlanNodeViewModel? node)
    {
        if (node == null || !PlanNodifyGraph.AllNodes.Contains(node)) return;
        try
        {
            PlanNodifyGraph.SelectKeyboardNode(node);
            GraphNodeDoubleClicked?.Invoke(sender ?? this, node);
            _detailsOriginNode = node;
            ShowDetails();
            AllPropertiesTab.IsSelected = true;
            Services.WorkspaceAccessibility.Focus(PlanPropertiesGrid);
        }
        catch (Exception exception) { Services.WorkspaceAccessibility.Report(exception, "OpenNodeDetails", this); }
    }
    private void DiagnosticIssuesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not PlanWorkspaceIssue issue || DataContext is not MainViewModel model) return;
        var current = model.PlanWorkspace.SelectedIssue;
        // Changing a statement rebuilds the list wrappers. WPF can select the replacement
        // wrapper while an evidence target is active; that is not a new diagnostic choice.
        bool sameDiagnostic = current?.Diagnostic is { } selected && issue.Diagnostic is { } candidate
            && selected.DiagnosticId == candidate.DiagnosticId && current.Location == issue.Location;
        if (!sameDiagnostic && !Equals(current, issue)) model.PlanWorkspace.SelectedIssue = issue;
        // Programmatic selection synchronization must not replace a narrow graph with details.
        if (sender is UIElement element && (element.IsKeyboardFocusWithin || element.IsMouseOver))
        {
            ShowDetails();
            StructuredDetailsTab.IsSelected = true;
        }
    }
    private void RecostGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecostGrid.SelectedItem is PlanNodeViewModel node) PlanNodifyGraph_NodeDoubleClicked(sender, node);
    }
    private void RecostGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && RecostGrid.SelectedItem is PlanNodeViewModel node)
        {
            PlanNodifyGraph_NodeDoubleClicked(sender, node);
            e.Handled = true;
        }
    }
    private void ReviewProposals_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainViewModel model || model.CurrentRewriteReview == null)
            {
                MessageBox.Show("当前没有可审核提案；请先分析单条语句的执行计划。", "SQL 改写提案");
                return;
            }
            new RewriteReviewWindow(model.CurrentRewriteSource, model.CurrentRewriteReview) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception exception)
        {
            string detail = Core.Diagnostics.ExceptionPolicy.Describe(exception, "RewriteReview.Open");
            MessageBox.Show(detail, "提案审核失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void CopyRefactoredSql_Click(object sender, RoutedEventArgs e) => CopyRefactoredSqlClicked?.Invoke(sender, e);
    private void CompareSql_Click(object sender, RoutedEventArgs e) => CompareSqlClicked?.Invoke(sender, e);
    private void CopyIndexDdl_Click(object sender, RoutedEventArgs e) => CopyIndexDdlClicked?.Invoke(sender, e);
    private void CopyDeploymentBundle_Click(object sender, RoutedEventArgs e) => CopyDeploymentBundleClicked?.Invoke(sender, e);
    private void CopyRollbackDdl_Click(object sender, RoutedEventArgs e) => CopyRollbackDdlClicked?.Invoke(sender, e);
}
