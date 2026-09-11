using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;

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
        private readonly IUnexpectedErrorReporter? _unexpectedErrors;

        public PlanComparisonUiActionService(
            Core.Services.PlanComparisonController comparisonController,
            Core.Services.PlanComparisonTreeService treeService,
            Core.Services.PlanComparisonTreeViewRenderer treeViewRenderer,
            Core.ViewModels.MainViewModel viewModel,
            TabControl mainTabControl,
            TreeView planATreeView,
            TreeView planBTreeView,
            XNamespace showplanNamespace,
            IUnexpectedErrorReporter? unexpectedErrors = null)
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
            _unexpectedErrors = unexpectedErrors;
        }

        public void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(_viewModel.PlanA)
                && e.PropertyName != nameof(_viewModel.PlanB)
                && e.PropertyName != nameof(_viewModel.ComparisonSelectionRevision))
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
            try
            {
                Core.Services.PlanComparisonResult comparison =
                    _comparisonController.BuildComparison(
                        planA,
                        planB,
                        showplanNamespace,
                        selection: ReferenceEquals(planA, _viewModel.PlanA) && ReferenceEquals(planB, _viewModel.PlanB)
                            ? _viewModel.ComparisonSelection : null);
                Core.Services.PlanComparisonTreeResult displayTree =
                    _treeService.BuildTree(comparison);
                // Prepare both trees before publishing either side.
                var nodesA = displayTree.StatementsA.Count > 0 ? displayTree.StatementsA
                    : displayTree.PlanA == null ? [] : new[] { displayTree.PlanA };
                var nodesB = displayTree.StatementsB.Count > 0 ? displayTree.StatementsB
                    : displayTree.PlanB == null ? [] : new[] { displayTree.PlanB };
                var treesA = nodesA.Select(_treeViewRenderer.Render).ToArray();
                var treesB = nodesB.Select(_treeViewRenderer.Render).ToArray();
                _planATreeView.Items.Clear();
                _planBTreeView.Items.Clear();
                foreach (var tree in treesA) _planATreeView.Items.Add(tree);
                foreach (var tree in treesB) _planBTreeView.Items.Add(tree);
                _viewModel.PublishComparison(comparison);
            }
            catch (Exception exception)
            {
                // PropertyChanged runs after the backing field is assigned. Never let
                // a comparison failure interrupt the second assignment in swap/load/clear.
                string detail = ExceptionPolicy.Describe(exception, "PlanComparisonUiActionService.RefreshCompareTrees", _unexpectedErrors);
                _viewModel.PublishComparison(null, detail);
                _planATreeView.Items.Clear();
                _planBTreeView.Items.Clear();
                _planATreeView.Items.Add(new TextBlock { Text = detail, TextWrapping = System.Windows.TextWrapping.Wrap });
                _planBTreeView.Items.Add(new TextBlock { Text = detail, TextWrapping = System.Windows.TextWrapping.Wrap });
            }
        }
    }
}
