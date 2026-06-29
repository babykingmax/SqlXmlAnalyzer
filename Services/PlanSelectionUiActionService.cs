using System;
using System.Linq;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanSelectionUiActionService
    {
        private readonly Core.Services.PlanSelectionActionService _selectionActionService;
        private readonly Core.Services.PlanPropertyService _planPropertyService;
        private readonly DataGrid _propertiesGrid;

        public PlanSelectionUiActionService(
            Core.Services.PlanSelectionActionService selectionActionService,
            Core.Services.PlanPropertyService planPropertyService,
            DataGrid propertiesGrid)
        {
            _selectionActionService = selectionActionService
                ?? throw new ArgumentNullException(nameof(selectionActionService));
            _planPropertyService = planPropertyService
                ?? throw new ArgumentNullException(nameof(planPropertyService));
            _propertiesGrid = propertiesGrid
                ?? throw new ArgumentNullException(nameof(propertiesGrid));
        }

        public void SelectFromOperatorTreeItem(object? selectedValue)
        {
            BindSelection(
                _selectionActionService.SelectFromOperatorTreeItem(selectedValue));
        }

        public void SelectFromVisualTreeNode(object? selectedValue)
        {
            BindSelection(
                _selectionActionService.SelectFromVisualTreeNode(selectedValue));
        }

        public void SelectFromGraphNode(object? selectedValue)
        {
            BindSelection(
                _selectionActionService.SelectFromGraphNode(selectedValue));
        }

        private void BindSelection(Core.Services.PlanSelectionResult result)
        {
            if (!result.HasSelection || result.RelOp == null)
            {
                return;
            }

            var properties = _planPropertyService.BuildProperties(result.RelOp).ToList();
            var view = new System.Windows.Data.ListCollectionView(properties);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Group"));
            _propertiesGrid.ItemsSource = view;
        }
    }
}
