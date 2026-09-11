using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly AnalysisSessionCoordinator? _sessions;
    private CancellationTokenSource? _renderCancellation;
    private long _renderRevision;
    private long? _documentRenderRequestId;
    private long? _ownedRenderRequestId;
    private bool _renderWasPartial;
    public Task<bool> PendingRender { get; private set; } = Task.FromResult(true);

    public IDisposable BeginDocumentRender(long requestId)
    {
        _documentRenderRequestId = requestId;
        return new DocumentRenderScope(this, requestId);
    }

    private sealed class DocumentRenderScope(PlanWorkspaceUiActionService owner, long requestId) : IDisposable
    {
        public void Dispose()
        {
            if (owner._documentRenderRequestId == requestId) owner._documentRenderRequestId = null;
        }
    }

    public async Task<bool> WaitForPendingRenderAsync()
    {
        long? requestId = _sessions?.Current?.RequestId;
        while (true)
        {
            var pending = PendingRender;
            bool applied = await pending;
            if (requestId != null && !_sessions!.IsCurrent(requestId.Value)) return false;
            if (_sessions?.Progress.State is AnalysisOperationState.Failed or AnalysisOperationState.Cancelled) return false;
            if (ReferenceEquals(pending, PendingRender)) return applied;
        }
    }

    public PlanWorkspaceUiActionService(MainViewModel model, Views.PlanWorkspaceView view,
        SqlDiffUiActionService diff, PlanStatisticsUiActionService statistics,
        PlanTreeService? tree = null, PlanOperatorTreeViewRenderer? renderer = null, PlanPropertyService? properties = null,
        AnalysisSessionCoordinator? sessions = null)
    {
        _model = model; _view = view; _diff = diff; _statistics = statistics;
        _tree = tree ?? new(); _renderer = renderer ?? new(); _properties = properties ?? new();
        _sessions = sessions;
        if (sessions != null) sessions.ProgressChanged += (_, progress) =>
        {
            if (progress.State is AnalysisOperationState.Loading or AnalysisOperationState.Cancelled or AnalysisOperationState.Idle)
                _renderCancellation?.Cancel();
        };
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
        if (_sessions != null && e.PropertyName == nameof(PlanWorkspaceViewModel.Selection))
        {
            PendingRender = RenderSelectionAsync();
            return;
        }
        if (!PendingRender.IsCompleted) return;
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

    private async Task<bool> RenderSelectionAsync()
    {
        _renderCancellation?.Cancel(); _renderCancellation?.Dispose();
        _renderCancellation = null;
        long revision = ++_renderRevision;
        var workspace = _model.PlanWorkspace;
        var selection = workspace.Selection;
        var document = workspace.Document;
        if (selection == null || document == null)
        {
            ClearPresentation();
            return true;
        }
        var active = _sessions!.Current;
        bool documentOwned = active != null && active.RequestId == _documentRenderRequestId && _sessions.Progress.IsBusy;
        bool continuingSelection = active != null && active.RequestId == _ownedRenderRequestId
            && _sessions.Progress.State == AnalysisOperationState.PreparingView;
        bool ownsOperation = !documentOwned;
        if (!continuingSelection) _renderWasPartial = _sessions.Progress.State == AnalysisOperationState.Partial;
        bool wasPartial = _renderWasPartial;
        var session = documentOwned || continuingSelection ? active! : _sessions.Begin(AnalysisDocumentKind.ExecutionPlanXml);
        if (ownsOperation) _ownedRenderRequestId = session.RequestId;
        _sessions.Report(session.RequestId, AnalysisOperationState.PreparingView);
        _renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        var token = _renderCancellation.Token;
        bool Current() => revision == _renderRevision && ReferenceEquals(workspace.Selection, selection) && _sessions.IsCurrent(session.RequestId);
        try
        {
            var report = workspace.Report;
            var options = _view.NodifyGraph.CaptureLoadOptions(selection.Operators, report, selection.MissingIndexes, token);
            XNamespace ns = InputRecognitionService.ShowPlanNamespace;
            var prepared = await Task.Run(() =>
            {
                var graph = new PlanGraphLoadUiActionService().Load(document, ns, new ObservableCollection<PlanNodeViewModel>(),
                    new ObservableCollection<ConnectionViewModel>(), options);
                token.ThrowIfCancellationRequested();
                var visualTree = _tree.BuildScopedVisualTree(selection.Operators, ns);
                var operatorTree = _tree.BuildScopedOperatorTree(selection.Operators, ns);
                token.ThrowIfCancellationRequested();
                return (Graph: graph, VisualTree: visualTree, OperatorTree: operatorTree, Xml: document.ToString());
            }, token);
            token.ThrowIfCancellationRequested();
            if (!Current()) return false;
            _updating = true;
            try
            {
                _view.PropertiesGrid.ItemsSource = null; _view.OperatorTree.Items.Clear(); _model.MissingIndexes.Clear();
                string sql = selection.Choice.Statement.Text ?? "未采集 StatementText。";
                _model.PlanStatementText = sql; _view.StatementTextBox.Text = sql;
                _model.PlanWarningsText = workspace.SelectedReportText; _view.XmlTextBox.Text = prepared.Xml;
                _view.VisualTree.ItemsSource = prepared.VisualTree;
                foreach (var root in prepared.OperatorTree) _view.OperatorTree.Items.Add(_renderer.RenderLazy(root));
                foreach (var index in selection.MissingIndexes) _model.MissingIndexes.Add(index);
                if (ownsOperation)
                {
                    _diff.SetSql(sql, workspace.Model?.Statements.Count == 1 ? _model.CurrentRewriteReview?.PreviewSql ?? sql : sql, null);
                    var source = selection.Choice.QueryPlan == null ? null : workspace.Model!.GetQueryPlanSource(selection.Choice.QueryPlan.Key);
                    _statistics.LoadFromPlan(source == null ? new XDocument() : new XDocument(new XElement(source)), ns);
                }
            }
            finally { _updating = false; }
            await _view.NodifyGraph.ApplyPreparedAsync(document, ns, prepared.Graph, token);
            if (!Current()) return false;
            PlanSourceTarget? sourceTarget;
            do
            {
                sourceTarget = workspace.SourceTarget;
                _updating = true;
                try { RenderSource(); }
                finally { _updating = false; }
                // Evidence navigation can replace a page while it is being committed.
                await _view.NodifyGraph.WaitForPendingPageAsync(token);
                if (!Current()) return false;
            } while (!ReferenceEquals(sourceTarget, workspace.SourceTarget));
            if (ownsOperation) _sessions.Report(session.RequestId,
                wasPartial || workspace.Report?.HasFailures == true ? AnalysisOperationState.Partial : AnalysisOperationState.Ready);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (revision == _renderRevision) ClearPresentation();
            return false;
        }
        catch (Exception exception)
        {
            _sessions.Fail(session.RequestId, exception);
            if (revision == _renderRevision) { ClearPresentation(); workspace.ReportFailure(exception); }
            return false;
        }
    }

    private void ClearPresentation()
    {
        _updating = true;
        try
        {
            _view.NodifyGraph.LoadFromExecutionPlan(new XDocument(), InputRecognitionService.ShowPlanNamespace);
            _view.OperatorTree.Items.Clear(); _view.VisualTree.ItemsSource = null; _view.PropertiesGrid.ItemsSource = null;
            _view.StatementTextBox.Clear(); _view.XmlTextBox.Clear(); _model.MissingIndexes.Clear();
            _model.PlanStatementText = ""; _model.PlanWarningsText = "";
            _model.CurrentRewriteReview = null; _model.CurrentRewriteSource = "";
            _diff.SetSql("", "", null);
            _statistics.LoadFromPlan(new XDocument(), InputRecognitionService.ShowPlanNamespace);
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
            selection?.Operators ?? Array.Empty<XElement>(), workspace.Report);
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
        if (item.Tag is XElement ancestor && source.Ancestors().Contains(ancestor)) item.IsExpanded = true;
        bool selected = ReferenceEquals(item.Tag, source);
        foreach (TreeViewItem child in item.Items) if (SelectTree(child, source)) { item.IsExpanded = true; selected = true; }
        item.IsSelected = ReferenceEquals(item.Tag, source);
        return selected;
    }
}
