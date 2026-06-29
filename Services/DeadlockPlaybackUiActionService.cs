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

        public DeadlockPlaybackUiActionService(
            Core.Services.DeadlockPlaybackStateService playbackStateService,
            Core.Services.DeadlockGraphVisualStateService visualStateService,
            DeadlockGraphPlaybackVisualService playbackVisualService,
            Core.Services.DeadlockStepBadgeService stepBadgeService,
            Core.Services.DeadlockGraphEdgeRegistryService edgeRegistryService,
            Canvas graphCanvas,
            Control playbackControl)
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
        }

        public void ShowPlayback(
            DeadlockPlaybackViewModel? playbackViewModel,
            Action updateVisibility)
        {
            _playbackControl.Visibility = Visibility.Visible;
            if (playbackViewModel != null)
            {
                playbackViewModel.CurrentStep = 0;
            }

            updateVisibility();
        }

        public void HidePlayback(
            DeadlockPlaybackViewModel? playbackViewModel,
            IReadOnlyDictionary<string, FrameworkElement> nodeElements,
            IReadOnlyDictionary<(string, string), DeadlockGraphEdgeElements> arrowCache,
            IReadOnlyList<Core.Services.DeadlockGraphEdge> edgesForDrawing,
            IEnumerable<Border> stepBadges)
        {
            _playbackControl.Visibility = Visibility.Collapsed;
            if (playbackViewModel != null)
            {
                playbackViewModel.IsPlaying = false;
            }

            foreach (FrameworkElement element in nodeElements.Values)
            {
                Core.Services.DeadlockGraphNodeVisualState resetState =
                    _visualStateService.CreateResetNodeState();
                _playbackVisualService.ApplyNodeVisualState(element, resetState);
            }

            foreach (KeyValuePair<(string, string), DeadlockGraphEdgeElements> edge in arrowCache)
            {
                bool isWaitEdge = _edgeRegistryService.IsWaitEdge(
                    edgesForDrawing,
                    edge.Key.Item1,
                    edge.Key.Item2);
                Core.Services.DeadlockGraphEdgeVisualState resetState =
                    _visualStateService.CreateResetEdgeState(isWaitEdge);
                _playbackVisualService.ApplyEdgeVisualState(edge.Value, resetState);
            }

            foreach (Border badge in stepBadges)
            {
                badge.Visibility = Visibility.Collapsed;
            }
        }

        public void UpdateGraphVisibility(
            DeadlockTimelineParser.ParsedDeadlock? currentTimeline,
            DeadlockPlaybackViewModel? playbackViewModel,
            bool isPlaybackEnabled,
            IReadOnlyDictionary<string, FrameworkElement> nodeElements,
            IReadOnlyDictionary<(string, string), DeadlockGraphEdgeElements> arrowCache,
            Dictionary<(string, string), Border> stepBadges)
        {
            if (currentTimeline == null || playbackViewModel == null || !isPlaybackEnabled)
            {
                return;
            }

            Core.Services.DeadlockPlaybackGraphState playbackState =
                _playbackStateService.BuildState(
                    currentTimeline,
                    playbackViewModel.CurrentStep,
                    playbackViewModel.FocusCriticalPath,
                    nodeElements.Keys,
                    CreatePlaybackEdgeKeys(arrowCache.Keys));

            foreach (KeyValuePair<string, FrameworkElement> node in nodeElements)
            {
                Core.Services.DeadlockPlaybackNodeState nodeState =
                    playbackState.Nodes[node.Key];
                Core.Services.DeadlockGraphNodeVisualState visualState =
                    _visualStateService.CreatePlaybackNodeState(nodeState);
                _playbackVisualService.ApplyNodeVisualState(node.Value, visualState);
            }

            foreach (KeyValuePair<(string, string), DeadlockGraphEdgeElements> edge in arrowCache)
            {
                ApplyEdgePlaybackState(edge, playbackState, stepBadges);
            }
        }

        private void ApplyEdgePlaybackState(
            KeyValuePair<(string, string), DeadlockGraphEdgeElements> edge,
            Core.Services.DeadlockPlaybackGraphState playbackState,
            Dictionary<(string, string), Border> stepBadges)
        {
            (string, string) idPair = edge.Key;
            var playbackEdgeKey =
                new Core.Services.DeadlockPlaybackEdgeKey(idPair.Item1, idPair.Item2);
            Core.Services.DeadlockPlaybackEdgeState edgeState =
                playbackState.Edges[playbackEdgeKey];
            Core.Services.DeadlockGraphEdgeVisualState visualState =
                _visualStateService.CreatePlaybackEdgeState(edgeState);

            _playbackVisualService.ApplyEdgeVisualState(edge.Value, visualState);
            if (!visualState.IsVisible || !visualState.BadgeStepNumber.HasValue)
            {
                if (stepBadges.TryGetValue(idPair, out Border? badge))
                {
                    badge.Visibility = Visibility.Collapsed;
                }

                return;
            }

            if (!stepBadges.TryGetValue(idPair, out Border? visibleBadge))
            {
                visibleBadge = _playbackVisualService.CreateStepBadge();
                stepBadges[idPair] = visibleBadge;
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

        private static IEnumerable<Core.Services.DeadlockPlaybackEdgeKey> CreatePlaybackEdgeKeys(
            IEnumerable<(string, string)> edgeKeys)
        {
            foreach ((string fromId, string toId) in edgeKeys)
            {
                yield return new Core.Services.DeadlockPlaybackEdgeKey(fromId, toId);
            }
        }
    }
}
