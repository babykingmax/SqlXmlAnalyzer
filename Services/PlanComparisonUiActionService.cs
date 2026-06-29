using System;
using System.Windows.Controls;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanComparisonUiActionService
    {
        private readonly Core.Services.PlanComparisonController _comparisonController;
        private readonly Core.Services.PlanComparisonTreeService _treeService;
        private readonly Core.Services.PlanComparisonTreeViewRenderer _treeViewRenderer;
        private readonly TreeView _planATreeView;
        private readonly TreeView _planBTreeView;

        public PlanComparisonUiActionService(
            Core.Services.PlanComparisonController comparisonController,
            Core.Services.PlanComparisonTreeService treeService,
            Core.Services.PlanComparisonTreeViewRenderer treeViewRenderer,
            TreeView planATreeView,
            TreeView planBTreeView)
        {
            _comparisonController = comparisonController
                ?? throw new ArgumentNullException(nameof(comparisonController));
            _treeService = treeService
                ?? throw new ArgumentNullException(nameof(treeService));
            _treeViewRenderer = treeViewRenderer
                ?? throw new ArgumentNullException(nameof(treeViewRenderer));
            _planATreeView = planATreeView
                ?? throw new ArgumentNullException(nameof(planATreeView));
            _planBTreeView = planBTreeView
                ?? throw new ArgumentNullException(nameof(planBTreeView));
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
