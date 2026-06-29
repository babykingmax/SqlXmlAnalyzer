using System;
using System.Collections.Generic;
using System.Windows.Controls;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Services
{
    internal sealed record DeadlockAnalysisUiResult(
        DeadlockTimelineParser.ParsedDeadlock Timeline,
        DeadlockPlaybackViewModel PlaybackViewModel,
        string StatusText);

    internal sealed class DeadlockAnalysisUiActionService
    {
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly ListBox _processesList;
        private readonly ListBox _resourcesList;
        private readonly ListBox _patternsList;
        private readonly Canvas _graphCanvas;
        private readonly Control _playbackControl;
        private readonly TabControl _mainTabControl;
        private readonly Action<DeadlockGraph> _drawGraph;
        private readonly EventHandler _playbackStepChangedHandler;
        private readonly Dictionary<(string, string), Border> _stepBadges;

        public DeadlockAnalysisUiActionService(
            Core.ViewModels.MainViewModel viewModel,
            ListBox processesList,
            ListBox resourcesList,
            ListBox patternsList,
            Canvas graphCanvas,
            Control playbackControl,
            TabControl mainTabControl,
            Action<DeadlockGraph> drawGraph,
            EventHandler playbackStepChangedHandler,
            Dictionary<(string, string), Border> stepBadges)
        {
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _processesList = processesList
                ?? throw new ArgumentNullException(nameof(processesList));
            _resourcesList = resourcesList
                ?? throw new ArgumentNullException(nameof(resourcesList));
            _patternsList = patternsList
                ?? throw new ArgumentNullException(nameof(patternsList));
            _graphCanvas = graphCanvas
                ?? throw new ArgumentNullException(nameof(graphCanvas));
            _playbackControl = playbackControl
                ?? throw new ArgumentNullException(nameof(playbackControl));
            _mainTabControl = mainTabControl
                ?? throw new ArgumentNullException(nameof(mainTabControl));
            _drawGraph = drawGraph
                ?? throw new ArgumentNullException(nameof(drawGraph));
            _playbackStepChangedHandler = playbackStepChangedHandler
                ?? throw new ArgumentNullException(nameof(playbackStepChangedHandler));
            _stepBadges = stepBadges
                ?? throw new ArgumentNullException(nameof(stepBadges));
        }

        public DeadlockAnalysisUiResult Apply(Core.Services.DeadlockDocumentResult documentResult)
        {
            ArgumentNullException.ThrowIfNull(documentResult);

            Core.DeadlockAnalysisOutput analysis = documentResult.Analysis;
            _viewModel.CurrentDeadlockDoc = documentResult.Document;
            _viewModel.ActivateWorkspace(Core.ViewModels.WorkspaceMode.Deadlock);
            _processesList.ItemsSource = analysis.Processes;
            _resourcesList.ItemsSource = analysis.Resources;
            _patternsList.ItemsSource = analysis.Patterns;

            var playbackViewModel = new DeadlockPlaybackViewModel(analysis.Timeline.Events);
            playbackViewModel.StepChanged += _playbackStepChangedHandler;
            _playbackControl.DataContext = playbackViewModel;

            foreach (Border badge in _stepBadges.Values)
            {
                _graphCanvas.Children.Remove(badge);
            }

            _stepBadges.Clear();
            _drawGraph(analysis.Graph);
            _mainTabControl.SelectedIndex = 0;

            foreach (string warning in analysis.Warnings)
            {
                Logger.Warning(warning);
            }

            string statusText = analysis.Warnings.Count == 0
                ? "死锁分析完成"
                : $"死锁分析完成（{analysis.Warnings.Count} 条警告）";

            return new DeadlockAnalysisUiResult(
                analysis.Timeline,
                playbackViewModel,
                statusText);
        }
    }
}
