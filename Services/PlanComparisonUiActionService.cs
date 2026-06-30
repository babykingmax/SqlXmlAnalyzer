using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanComparisonUiActionService
    {
        private readonly Core.Services.PlanComparisonController _comparisonController;
        private readonly Core.Services.PlanComparisonTreeService _treeService;
        private readonly Core.Services.PlanComparisonTreeViewRenderer _treeViewRenderer;
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly TabControl _mainTabControl;
        private readonly TreeView _planATreeView;
        private readonly TreeView _planBTreeView;
        private readonly XNamespace _showplanNamespace;

        public PlanComparisonUiActionService(
            Core.Services.PlanComparisonController comparisonController,
            Core.Services.PlanComparisonTreeService treeService,
            Core.Services.PlanComparisonTreeViewRenderer treeViewRenderer,
            Core.ViewModels.MainViewModel viewModel,
            TabControl mainTabControl,
            TreeView planATreeView,
            TreeView planBTreeView,
            XNamespace showplanNamespace)
        {
            _comparisonController = comparisonController
                ?? throw new ArgumentNullException(nameof(comparisonController));
            _treeService = treeService
                ?? throw new ArgumentNullException(nameof(treeService));
            _treeViewRenderer = treeViewRenderer
                ?? throw new ArgumentNullException(nameof(treeViewRenderer));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _mainTabControl = mainTabControl
                ?? throw new ArgumentNullException(nameof(mainTabControl));
            _planATreeView = planATreeView
                ?? throw new ArgumentNullException(nameof(planATreeView));
            _planBTreeView = planBTreeView
                ?? throw new ArgumentNullException(nameof(planBTreeView));
            _showplanNamespace = showplanNamespace
                ?? throw new ArgumentNullException(nameof(showplanNamespace));
        }

        public void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(_viewModel.PlanA)
                && e.PropertyName != nameof(_viewModel.PlanB))
            {
                return;
            }

            RefreshCompareTrees();
            if (_viewModel.PlanA != null && _viewModel.PlanB != null)
            {
                _viewModel.ActivateWorkspace(Core.ViewModels.WorkspaceMode.Compare);
                var tab = _mainTabControl.Items
                    .OfType<TabItem>()
                    .FirstOrDefault(t => t.Header?.ToString()?.Contains("A/B") == true);
                if (tab != null)
                {
                    _mainTabControl.SelectedItem = tab;
                }
            }
        }

        public void RefreshCompareTrees()
        {
            RefreshCompareTrees(
                _viewModel.PlanA,
                _viewModel.PlanB,
                _showplanNamespace);
        }

        public void RefreshCompareTrees(
            Core.ViewModels.PlanSnapshot? planA,
            Core.ViewModels.PlanSnapshot? planB,
            XNamespace showplanNamespace)
        {
            _planATreeView.Items.Clear();
            _planBTreeView.Items.Clear();

            Core.Services.PlanComparisonResult comparison =
                _comparisonController.BuildComparison(
                    planA,
                    planB,
                    showplanNamespace);
            Core.Services.PlanComparisonTreeResult displayTree =
                _treeService.BuildTree(comparison);

            if (displayTree.PlanA != null)
            {
                _planATreeView.Items.Add(
                    _treeViewRenderer.Render(displayTree.PlanA));
            }

            if (displayTree.PlanB != null)
            {
                _planBTreeView.Items.Add(
                    _treeViewRenderer.Render(displayTree.PlanB));
            }
        }
    }
}
