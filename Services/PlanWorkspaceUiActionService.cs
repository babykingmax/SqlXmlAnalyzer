using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Services;

internal sealed class PlanWorkspaceUiActionService
{
    private readonly MainViewModel _model;
    private readonly Views.PlanWorkspaceView _view;
    private readonly SqlDiffUiActionService _diff;
    private readonly PlanStatisticsUiActionService _statistics;
    private readonly PlanTreeService _tree;
    private readonly PlanOperatorTreeViewRenderer _renderer;
    private readonly PlanPropertyService _properties;
    private bool _updating;

    public PlanWorkspaceUiActionService(MainViewModel model, Views.PlanWorkspaceView view,
        SqlDiffUiActionService diff, PlanStatisticsUiActionService statistics,
        PlanTreeService? tree = null, PlanOperatorTreeViewRenderer? renderer = null, PlanPropertyService? properties = null)
    {
        _model = model; _view = view; _diff = diff; _statistics = statistics;
        _tree = tree ?? new(); _renderer = renderer ?? new(); _properties = properties ?? new();
        model.PlanWorkspace.PropertyChanged += Changed;
        view.GraphNodeSelected += (_, node) => Navigate(node?.RawElement);
        view.OperatorTreeSelectedItemChanged += (_, e) => Navigate((e.NewValue as TreeViewItem)?.Tag as XElement);
        view.VisualTreeSelectedItemChanged += (_, e) => Navigate((e.NewValue as PlanVisualNode)?.Tag);
        view.RecostDataGrid.SelectionChanged += (_, _) => Navigate((view.RecostDataGrid.SelectedItem as PlanNodeViewModel)?.RawElement);
    }

    private void Navigate(XElement? element)
    {
        if (!_updating && element != null && _model.PlanWorkspace.Selection?.Operators.Contains(element) == true)
            _model.PlanWorkspace.NavigateOperator(element);
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (_updating || (e.PropertyName != nameof(PlanWorkspaceViewModel.Selection)
            && e.PropertyName != nameof(PlanWorkspaceViewModel.SourceTarget))) return;
        _updating = true;
        try
        {
            if (e.PropertyName == nameof(PlanWorkspaceViewModel.Selection)) RenderSelection();
            else RenderSource();
        }
        catch (Exception exception)
        {
            // Drop all visible data before reporting a failed render, so no stale node appears selected.
            _view.NodifyGraph.LoadFromExecutionPlan(new XDocument(), InputRecognitionService.ShowPlanNamespace);
            _view.OperatorTree.Items.Clear(); _view.VisualTree.ItemsSource = null; _view.PropertiesGrid.ItemsSource = null;
            _view.StatementTextBox.Clear(); _model.MissingIndexes.Clear();
            _model.PlanStatementText = ""; _model.PlanWarningsText = ""; _model.CurrentRewriteReview = null;
            _diff.SetSql("", "", null);
            _statistics.LoadFromPlan(new XDocument(), InputRecognitionService.ShowPlanNamespace);
            _model.PlanWorkspace.ReportFailure(exception);
        }
        finally { _updating = false; }
    }

    private void RenderSelection()
    {
        var workspace = _model.PlanWorkspace;
        var selection = workspace.Selection;
        XNamespace ns = InputRecognitionService.ShowPlanNamespace;
        _view.PropertiesGrid.ItemsSource = null;
        _view.OperatorTree.Items.Clear();
        _view.VisualTree.ItemsSource = null;
        _model.MissingIndexes.Clear();
        string sql = selection?.Choice.Statement.Text ?? (selection == null ? "" : "未采集 StatementText。");
        _model.PlanStatementText = sql;
        _view.StatementTextBox.Text = sql;
        _model.PlanWarningsText = workspace.SelectedReportText;
        _view.XmlTextBox.Text = workspace.Document?.ToString() ?? "";
        _view.NodifyGraph.LoadFromExecutionPlan(workspace.Document ?? new XDocument(), ns,
            selection?.Operators ?? Array.Empty<XElement>());
        _view.NodifyGraph.SelectedNode = null;
        bool singleStatement = workspace.Model?.Statements.Count == 1;
        if (!singleStatement) { _model.CurrentRewriteReview = null; _model.CurrentRewriteSource = ""; }
        _diff.SetSql(sql, singleStatement ? _model.CurrentRewriteReview?.PreviewSql ?? sql : sql, null);
        if (selection == null) { _statistics.LoadFromPlan(new XDocument(), ns); return; }
        _view.VisualTree.ItemsSource = _tree.BuildScopedVisualTree(selection.Operators, ns);
        foreach (var root in _tree.BuildScopedOperatorTree(selection.Operators, ns))
            _view.OperatorTree.Items.Add(_renderer.Render(root));
        foreach (var index in selection.MissingIndexes) _model.MissingIndexes.Add(index);
        // Statistics only needs a read-only presentation copy of this QueryPlan, never an identity lookup.
        var source = selection.Choice.QueryPlan == null ? null : workspace.Model!.GetQueryPlanSource(selection.Choice.QueryPlan.Key);
        _statistics.LoadFromPlan(source == null ? new XDocument() : new XDocument(new XElement(source)), ns);
    }

    private void RenderSource()
    {
        var target = _model.PlanWorkspace.SourceTarget;
        if (target?.Location.Operator is not { } key)
        {
            _view.NodifyGraph.SelectedNode = null; _view.RecostDataGrid.SelectedItem = null;
            _view.PropertiesGrid.ItemsSource = null; return;
        }
        _view.NodifyGraph.SelectOperator(key);
        var node = _view.NodifyGraph.SelectedNode;
        _view.RecostDataGrid.SelectedItem = node;
        if (node?.RawElement != null)
        {
            var properties = new ListCollectionView(_properties.BuildProperties(node.RawElement).ToList());
            properties.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            _view.PropertiesGrid.ItemsSource = properties;
            foreach (TreeViewItem root in _view.OperatorTree.Items) SelectTree(root, node.RawElement);
        }
    }
    private static bool SelectTree(TreeViewItem item, XElement source)
    {
        bool selected = ReferenceEquals(item.Tag, source);
        foreach (TreeViewItem child in item.Items) if (SelectTree(child, source)) { item.IsExpanded = true; selected = true; }
        item.IsSelected = ReferenceEquals(item.Tag, source);
        return selected;
    }
}
