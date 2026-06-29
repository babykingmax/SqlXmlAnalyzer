using System;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class AnalysisResultsUiActionService
    {
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly Canvas _deadlockGraphCanvas;
        private readonly ListBox _deadlockProcessesList;
        private readonly ListBox _deadlockResourcesList;
        private readonly ListBox _deadlockPatternsListBox;
        private readonly TreeView _planOperatorTree;
        private readonly TextBlock _statusTextBlock;

        public AnalysisResultsUiActionService(
            Core.ViewModels.MainViewModel viewModel,
            Canvas deadlockGraphCanvas,
            ListBox deadlockProcessesList,
            ListBox deadlockResourcesList,
            ListBox deadlockPatternsListBox,
            TreeView planOperatorTree,
            TextBlock statusTextBlock)
        {
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _deadlockGraphCanvas = deadlockGraphCanvas
                ?? throw new ArgumentNullException(nameof(deadlockGraphCanvas));
            _deadlockProcessesList = deadlockProcessesList
                ?? throw new ArgumentNullException(nameof(deadlockProcessesList));
            _deadlockResourcesList = deadlockResourcesList
                ?? throw new ArgumentNullException(nameof(deadlockResourcesList));
            _deadlockPatternsListBox = deadlockPatternsListBox
                ?? throw new ArgumentNullException(nameof(deadlockPatternsListBox));
            _planOperatorTree = planOperatorTree
                ?? throw new ArgumentNullException(nameof(planOperatorTree));
            _statusTextBlock = statusTextBlock
                ?? throw new ArgumentNullException(nameof(statusTextBlock));
        }

        public void ClearResults()
        {
            _viewModel.ClearResults();
            _deadlockGraphCanvas.Children.Clear();
            _deadlockProcessesList.ItemsSource = null;
            _deadlockResourcesList.ItemsSource = null;
            _deadlockPatternsListBox.ItemsSource = null;
            _planOperatorTree.Items.Clear();
            _deadlockPatternsListBox.Items.Clear();
            _statusTextBlock.Text = "结果已清空";
        }
    }
}
