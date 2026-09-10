using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Services;

internal sealed class DeadlockWorkspaceUiActionService
{
    private readonly MainViewModel _model;
    private readonly Views.DeadlockWorkspaceView _view;
    private readonly DeadlockGraphUiState _graph;
    private readonly Core.Services.DeadlockSelectionDetailService _details = new();
    private bool _updating;
    public DeadlockWorkspaceUiActionService(MainViewModel model, Views.DeadlockWorkspaceView view, DeadlockGraphUiState graph)
    {
        _model = model; _view = view; _graph = graph;
        model.DeadlockWorkspace.PropertyChanged += Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        _model.DeadlockWorkspace.IsSynchronizingView = true;
        try
        {
            if (e.PropertyName == nameof(DeadlockWorkspaceViewModel.Analysis) && _model.DeadlockWorkspace.Analysis == null)
            {
                ClearVisuals();
            }
            if (e.PropertyName != nameof(DeadlockWorkspaceViewModel.SourceTarget)) return;
            foreach (var node in _graph.NodeElements.Values) node.Effect = null;
            foreach (var edge in _graph.ArrowCache.Values) edge.Line.Effect = null;
            var target = _model.DeadlockWorkspace.SourceTarget;
            _view.ProcessesList.SelectedItem = target?.Process;
            _view.ResourcesList.SelectedItem = target?.Resource;
            if (target == null) return;
            if (_model.DeadlockWorkspace.SelectedEvidence != null && _model.DeadlockWorkspace.SelectedPattern is { } pattern)
                _model.DeadlockPatternText = _details.BuildPatternDetail(pattern);
            else if (target.Process != null) _model.DeadlockPatternText = _details.BuildProcessDetail(target.Process);
            else if (target.Resource != null) _model.DeadlockPatternText = _details.BuildResourceDetail(target.Resource);
            if (target.Process != null)
            {
                Highlight("proc_id_" + target.Process.Id); _view.ProcessesList.ScrollIntoView(target.Process);
            }
            foreach (string processId in target.ProcessIds) Highlight("proc_id_" + processId);
            if (target.Resource != null)
            {
                int index = _model.DeadlockWorkspace.Analysis!.Resources.IndexOf(target.Resource);
                Highlight("res_single_" + index); _view.ResourcesList.ScrollIntoView(target.Resource);
            }
            foreach (var edge in _graph.EdgesForDrawing.Where(edge => target.LinkIds.Contains(edge.EvidenceId)))
                if (_graph.ArrowCache.TryGetValue(edge.Key, out var drawing)) drawing.Line.Effect = HighlightEffect();
        }
        catch (Exception exception) { ClearVisuals(); _model.DeadlockWorkspace.ReportFailure(exception); }
        finally { _model.DeadlockWorkspace.IsSynchronizingView = false; _updating = false; }
    }
    private void ClearVisuals()
    {
        _model.CurrentDeadlockDoc = null; _model.CurrentDeadlockAnalysis = null; _model.DeadlockPatternText = "";
        if (_view.Playback.DataContext is ViewModels.DeadlockPlaybackViewModel playback) playback.IsPlaying = false;
        _view.Playback.DataContext = null;
        _view.ProcessesList.ItemsSource = null; _view.ResourcesList.ItemsSource = null; _view.PatternsListBox.ItemsSource = null;
        _view.GraphCanvas.Children.Clear(); _graph.NodeElements.Clear(); _graph.NodePositions.Clear();
        _graph.EdgesForDrawing.Clear(); _graph.ArrowCache.Clear(); _graph.StepBadges.Clear(); _graph.ResourceGroupDetails.Clear();
    }
    private void Highlight(string key)
    {
        if (_graph.NodeElements.TryGetValue(key, out var node)) node.Effect = HighlightEffect();
    }
    private static DropShadowEffect HighlightEffect() => new() { Color = Colors.DeepSkyBlue, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 };
}
