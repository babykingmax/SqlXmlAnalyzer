using System;
using System.Windows;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DocumentRefreshUiActionService
    {
        private readonly Core.Services.DocumentRefreshActionService _refreshActionService;
        private readonly Action<string> _analyzeFile;
        private readonly Core.ViewModels.MainViewModel _viewModel;

        public DocumentRefreshUiActionService(
            Core.Services.DocumentRefreshActionService refreshActionService,
            Action<string> analyzeFile,
            Core.ViewModels.MainViewModel viewModel)
        {
            _refreshActionService = refreshActionService
                ?? throw new ArgumentNullException(nameof(refreshActionService));
            _analyzeFile = analyzeFile
                ?? throw new ArgumentNullException(nameof(analyzeFile));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public void RefreshDeadlockGraph()
        {
            RefreshDeadlockGraph(_viewModel.CurrentDeadlockFilePath);
        }

        public void RefreshPlanGraph()
        {
            RefreshPlanGraph(_viewModel.CurrentPlanFilePath);
        }

        public void RefreshDeadlockGraph(string? currentFilePath)
        {
            RefreshDocument(
                _refreshActionService.BuildDeadlockRefresh(currentFilePath));
        }

        public void RefreshPlanGraph(string? currentFilePath)
        {
            RefreshDocument(
                _refreshActionService.BuildPlanRefresh(currentFilePath));
        }

        private void RefreshDocument(
            Core.Services.DocumentRefreshActionResult result)
        {
            if (result.Status == Core.Services.DocumentRefreshActionStatus.MissingFile)
            {
                MessageBox.Show(result.UserMessage, "鎻愮ず", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _analyzeFile(result.FilePath);
        }
    }
}
