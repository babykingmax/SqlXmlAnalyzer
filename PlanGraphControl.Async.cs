using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer;

public partial class PlanGraphControl
{
    private CancellationTokenSource? _pageCancellation;
    private int _pageStart;
    private long _pageRevision;
    private long _graphRevision;
    private CancellationToken _graphToken;
    public IReadOnlyList<PlanNodeViewModel> AllNodes => _masterNodes;
    public string PageDescription { get; private set; } = "未采集算子";
    public bool CanPreviousPage => _pageStart > 0;
    public bool CanNextPage => _pageStart + PlanGraphPageService.MaximumVisibleNodes < _masterNodes.Count(n => n.IsVisible);
    public Task PendingPage { get; private set; } = Task.CompletedTask;

    internal PlanGraphLoadUiActionOptions CaptureLoadOptions(IReadOnlyList<XElement> operators,
        Core.Rules.PlanDiagnosticReport? diagnostics, IReadOnlyList<Core.Models.MissingIndexSuggestion> indexes, CancellationToken token) => new()
    {
        Operators = operators, Diagnostics = diagnostics, MissingIndexes = indexes, CancellationToken = token,
        InitialLayout = LayoutMode, InitialColor = ColorMode, InitialView = (DiagramViewMode)CmbViewMode.SelectedIndex,
        InitialLinkMetric = LinkMetric, ResidualIoThreshold = ResidualIOThreshold, ResidualIoMinRowsRead = ResidualIOMinRowsRead
    };

    internal async Task ApplyPreparedAsync(XDocument document, XNamespace ns, PlanGraphLoadUiActionResult result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        InvalidateGraph();
        long revision = _graphRevision;
        _graphToken = token;
        _currentDoc = document; _currentNs = ns;
        _masterNodes = result.MasterNodes.ToList(); _masterConnections = result.MasterConnections.ToList();
        ApplyAutomaticViewMode();
        // Controls can change while the graph is prepared on a worker. Commit
        // every model with the currently selected presentation options.
        ModeUiActionService.ApplyViewMode(CmbViewMode.SelectedIndex, _masterNodes);
        ReapplyColorMode();
        ReapplyLinkMetric();
        foreach (var connection in _masterConnections) connection.LayoutMode = LayoutMode;
        if (_masterNodes.Count <= PlanGraphPageService.MaximumVisibleNodes)
            LayoutUiActionService.ReapplyLayout(document, ns, _masterNodes, _masterConnections, LayoutMode);
        OnPropertyChanged(nameof(AllNodes));
        SelectedNode = null;
        EmptyHint.Text = "当前选择未采集可显示的算子；空图不构成健康结论。";
        ShowEmptyHint(!result.HasGraph);
        PendingPage = ShowPageAsync(0, token);
        await WaitForPendingPageAsync(token);
        token.ThrowIfCancellationRequested();
        if (revision != _graphRevision) throw new OperationCanceledException("计划图已被替换。", token);
        // The request owns preparation and the initial commit, not the lifetime
        // of a completed view. Opening another workspace cancels that request,
        // but this graph must remain navigable until it is replaced or cleared.
        _graphToken = CancellationToken.None;
    }

    internal async Task WaitForPendingPageAsync(CancellationToken token)
    {
        long revision = _graphRevision;
        while (true)
        {
            var pending = PendingPage;
            try { await pending; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && revision == _graphRevision
                && !ReferenceEquals(pending, PendingPage))
            {
                // A newer page superseded this page, not the document load.
                continue;
            }
            token.ThrowIfCancellationRequested();
            if (revision != _graphRevision) throw new OperationCanceledException("计划图已被替换。", token);
            if (ReferenceEquals(pending, PendingPage)) return;
        }
    }

    private void InvalidateGraph()
    {
        _graphRevision++;
        _viewportIntentRevision++;
        _fitPending = true;
        _pendingViewportNode = null;
        _pageIsCommitting = false;
        _keepViewportOnPageCommit = false;
        _searchIndex = -1;
        SearchResultText.Text = "";
        _graphToken = CancellationToken.None;
        InvalidatePage();
        PendingPage = Task.CompletedTask;
    }

    private void InvalidatePage()
    {
        _pageRevision++;
        _pageCancellation?.Cancel(); _pageCancellation?.Dispose(); _pageCancellation = null;
    }

    private async Task ShowPageAsync(int index, CancellationToken token, PlanNodeViewModel? priorityNode = null)
    {
        InvalidatePage();
        long revision = _pageRevision;
        _pageIsCommitting = true;
        _keepViewportOnPageCommit = false;
        _pendingViewportNode = priorityNode;
        _pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pageToken = _pageCancellation.Token;
        try
        {
            var candidates = _masterNodes.Where(n => n.IsVisible).ToArray();
            var page = PlanGraphPageService.ForIndex(candidates.Length, index);
            _pageStart = page.Start;
            var visible = candidates.Skip(page.Start).Take(page.Count).ToArray();
            var included = visible.ToHashSet();
            var edges = _masterConnections.Where(c => c.Source != null && c.Target != null && included.Contains(c.Source) && included.Contains(c.Target) && c.IsVisible).ToArray();
            RefreshPageContext(visible);
            Nodes.Clear(); Connections.Clear();
            PageDescription = page.Description;
            OnPropertyChanged(nameof(PageDescription)); OnPropertyChanged(nameof(CanPreviousPage)); OnPropertyChanged(nameof(CanNextPage));
            // A direct evidence target is available immediately; the remaining
            // containers are committed in small dispatcher batches.
            if (priorityNode != null && included.Contains(priorityNode)) Nodes.Add(priorityNode);
            if (visible.Length > 0 && _masterNodes.Count > PlanGraphPageService.MaximumVisibleNodes)
            {
                var elements = visible.Where(n => n.RawElement != null).Select(n => n.RawElement!).ToArray();
                var collapsed = visible.Where(n => n.IsCollapsed && n.RawElement != null).Select(n => n.RawElement!).ToHashSet();
                var direction = LayoutMode == PlanLayoutMode.Horizontal ? PlanGraphLayoutDirection.Horizontal : PlanGraphLayoutDirection.Vertical;
                var positions = await Task.Run(() => new PlanGraphLayoutService().CalculateLayout(elements, _currentNs!, collapsed, direction), pageToken);
                pageToken.ThrowIfCancellationRequested();
                if (revision != _pageRevision) throw new OperationCanceledException(pageToken);
                var map = visible.ToDictionary(n => n.RawElement!);
                foreach (var position in positions) map[position.Element].Location = new Point(position.X, position.Y);
            }
            await UiBatchScheduler.ApplyAsync(visible.Where(n => !ReferenceEquals(n, priorityNode)).ToArray(), Nodes.Add, pageToken, () => revision == _pageRevision);
            await UiBatchScheduler.ApplyAsync(edges, Connections.Add, pageToken, () => revision == _pageRevision);
            var anchor = priorityNode ?? (SelectedNode != null && included.Contains(SelectedNode) ? SelectedNode : visible.FirstOrDefault());
            if (revision == _pageRevision) CompletePageViewport(anchor);
            Logger.Debug($"IMP26 graph page committed: visible={visible.Length}, total={candidates.Length}.");
        }
        finally
        {
            if (revision == _pageRevision) _pageIsCommitting = false;
        }
    }

    private async void PreviousGraphPage_Click(object sender, RoutedEventArgs e) => await NavigatePageAsync(_pageStart - PlanGraphPageService.MaximumVisibleNodes);
    private async void NextGraphPage_Click(object sender, RoutedEventArgs e) => await NavigatePageAsync(_pageStart + PlanGraphPageService.MaximumVisibleNodes);
    public async Task NavigatePageAsync(int index)
    {
        try { PendingPage = ShowPageAsync(index, _graphToken); await PendingPage; }
        catch (OperationCanceledException) { }
        catch (Exception exception) { WorkspaceAccessibility.Report(exception, "GraphPage", this); }
    }

    private void RevealPage(PlanNodeViewModel node)
    {
        if (Nodes.Contains(node)) return;
        int index = Array.IndexOf(_masterNodes.Where(n => n.IsVisible).ToArray(), node);
        if (index >= 0) _ = RevealPageAsync(index, node);
    }
    private async Task RevealPageAsync(int index, PlanNodeViewModel node)
    {
        try { PendingPage = ShowPageAsync(index, _graphToken, node); await PendingPage; }
        catch (OperationCanceledException) { }
        catch (Exception exception) { WorkspaceAccessibility.Report(exception, "GraphReveal", this); }
    }
}
