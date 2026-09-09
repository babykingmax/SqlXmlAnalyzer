using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockPlaybackUiActionService
    {
        private readonly Core.Services.DeadlockPlaybackStateService _playbackStateService;
        private readonly Core.Services.DeadlockGraphVisualStateService _visualStateService;
        private readonly DeadlockGraphPlaybackVisualService _playbackVisualService;
        private readonly Core.Services.DeadlockStepBadgeService _stepBadgeService;
        private readonly Core.Services.DeadlockGraphEdgeRegistryService _edgeRegistryService;
        private readonly Canvas _graphCanvas;
        private readonly Control _playbackControl;
        private readonly DeadlockGraphUiState _graphState;
        private DeadlockTimelineParser.ParsedDeadlock? _currentTimeline;
        private DeadlockPlaybackViewModel? _playbackViewModel;

        public DeadlockPlaybackUiActionService(
            Core.Services.DeadlockPlaybackStateService playbackStateService,
            Core.Services.DeadlockGraphVisualStateService visualStateService,
            DeadlockGraphPlaybackVisualService playbackVisualService,
            Core.Services.DeadlockStepBadgeService stepBadgeService,
            Core.Services.DeadlockGraphEdgeRegistryService edgeRegistryService,
            Canvas graphCanvas,
            Control playbackControl,
            DeadlockGraphUiState graphState)
        {
            _playbackStateService = playbackStateService
                ?? throw new ArgumentNullException(nameof(playbackStateService));
            _visualStateService = visualStateService
                ?? throw new ArgumentNullException(nameof(visualStateService));
            _playbackVisualService = playbackVisualService
                ?? throw new ArgumentNullException(nameof(playbackVisualService));
            _stepBadgeService = stepBadgeService
                ?? throw new ArgumentNullException(nameof(stepBadgeService));
            _edgeRegistryService = edgeRegistryService
                ?? throw new ArgumentNullException(nameof(edgeRegistryService));
            _graphCanvas = graphCanvas ?? throw new ArgumentNullException(nameof(graphCanvas));
            _playbackControl = playbackControl
                ?? throw new ArgumentNullException(nameof(playbackControl));
            _graphState = graphState ?? throw new ArgumentNullException(nameof(graphState));
        }

        public void SetCurrentPlayback(
            DeadlockTimelineParser.ParsedDeadlock? currentTimeline,
            DeadlockPlaybackViewModel? playbackViewModel)
        {
            _currentTimeline = currentTimeline;
            _playbackViewModel = playbackViewModel;
        }

        public void ShowPlayback(Action updateVisibility)
        {
            _playbackControl.Visibility = Visibility.Visible;
            if (_playbackViewModel != null)
            {
                _playbackViewModel.CurrentStep = 0;
            }

            updateVisibility();
        }

        public void HidePlayback()
        {
            _playbackControl.Visibility = Visibility.Collapsed;
            if (_playbackViewModel != null)
            {
                _playbackViewModel.IsPlaying = false;
            }

            foreach (var node in _graphState.NodeElements)
            {
                string processId = node.Key.StartsWith("proc_id_", StringComparison.Ordinal) ? node.Key["proc_id_".Length..] : "";
                var process = _currentTimeline?.Processes.GetValueOrDefault(processId);
                Core.Services.DeadlockGraphNodeVisualState resetState =
                    _visualStateService.CreateResetNodeState(process?.IsVictim == true, process?.IsInCycle == true);
                _playbackVisualService.ApplyNodeVisualState(node.Value, resetState);
            }

            foreach (var edge in _graphState.ArrowCache)
            {
                bool isWaitEdge = _edgeRegistryService.IsWaitEdge(
                    _graphState.EdgesForDrawing,
                    edge.Key.FromId,
                    edge.Key.ToId);
                Core.Services.DeadlockGraphEdgeVisualState resetState =
                    _visualStateService.CreateResetEdgeState(isWaitEdge);
                _playbackVisualService.ApplyEdgeVisualState(edge.Value, resetState);
            }

            foreach (Border badge in _graphState.StepBadges.Values)
            {
                badge.Visibility = Visibility.Collapsed;
            }
        }

        public void UpdateGraphVisibility(bool isPlaybackEnabled)
        {
            if (_currentTimeline == null || _playbackViewModel == null || !isPlaybackEnabled)
            {
                return;
            }

            Core.Services.DeadlockPlaybackGraphState playbackState =
                _playbackStateService.BuildState(
                    _currentTimeline,
                    _playbackViewModel.CurrentStep,
                    _playbackViewModel.FocusCriticalPath,
                    _graphState.NodeElements.Keys,
                    _graphState.ArrowCache.Keys);

            foreach (KeyValuePair<string, FrameworkElement> node in _graphState.NodeElements)
            {
                Core.Services.DeadlockPlaybackNodeState nodeState =
                    playbackState.Nodes[node.Key];
                Core.Services.DeadlockGraphNodeVisualState visualState =
                    _visualStateService.CreatePlaybackNodeState(nodeState);
                _playbackVisualService.ApplyNodeVisualState(node.Value, visualState);
            }

            foreach (var edge in _graphState.ArrowCache)
            {
                ApplyEdgePlaybackState(edge, playbackState);
            }
        }

        private void ApplyEdgePlaybackState(
            KeyValuePair<Core.Services.DeadlockPlaybackEdgeKey, DeadlockGraphEdgeElements> edge,
            Core.Services.DeadlockPlaybackGraphState playbackState)
        {
            var playbackEdgeKey = edge.Key;
            Core.Services.DeadlockPlaybackEdgeState edgeState =
                playbackState.Edges[playbackEdgeKey];
            Core.Services.DeadlockGraphEdgeVisualState visualState =
                _visualStateService.CreatePlaybackEdgeState(edgeState);

            _playbackVisualService.ApplyEdgeVisualState(edge.Value, visualState);
            if (!visualState.IsVisible || !visualState.BadgeStepNumber.HasValue)
            {
                if (_graphState.StepBadges.TryGetValue(playbackEdgeKey, out Border? badge))
                {
                    badge.Visibility = Visibility.Collapsed;
                }

                return;
            }

            if (!_graphState.StepBadges.TryGetValue(playbackEdgeKey, out Border? visibleBadge))
            {
                visibleBadge = _playbackVisualService.CreateStepBadge();
                _graphState.StepBadges[playbackEdgeKey] = visibleBadge;
                _graphCanvas.Children.Add(visibleBadge);
            }

            _playbackVisualService.ApplyStepBadgePlacement(
                visibleBadge,
                _stepBadgeService.PlaceBadge(
                    visualState.BadgeStepNumber.Value,
                    edge.Value.Line.X1,
                    edge.Value.Line.Y1,
                    edge.Value.Line.X2,
                    edge.Value.Line.Y2));
            visibleBadge.Visibility = Visibility.Visible;
        }

    }
}
