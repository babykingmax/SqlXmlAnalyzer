using System;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class WorkspacePanelUiActionService
    {
        private readonly Core.Services.WorkspacePanelLayoutService _layoutService;
        private readonly ColumnDefinition _originalSqlColumn;
        private readonly ColumnDefinition _sqlSplitterColumn;
        private readonly GridSplitter _sqlGridSplitter;
        private readonly Button _compareSqlButton;
        private readonly ColumnDefinition _deadlockLeftColumn;
        private readonly ColumnDefinition _deadlockRightColumn;
        private readonly Button _deadlockLeftButton;
        private readonly Button _deadlockRightButton;
        private readonly Grid _planContentGrid;
        private System.Windows.GridLength _planLeftColumnWidth = new(320);
        private System.Windows.GridLength _planRightColumnWidth = new(280);

        public WorkspacePanelUiActionService(
            Core.Services.WorkspacePanelLayoutService layoutService,
            ColumnDefinition originalSqlColumn,
            ColumnDefinition sqlSplitterColumn,
            GridSplitter sqlGridSplitter,
            Button compareSqlButton,
            ColumnDefinition deadlockLeftColumn,
            ColumnDefinition deadlockRightColumn,
            Button deadlockLeftButton,
            Button deadlockRightButton,
            Grid planContentGrid)
        {
            _layoutService = layoutService
                ?? throw new ArgumentNullException(nameof(layoutService));
            _originalSqlColumn = originalSqlColumn
                ?? throw new ArgumentNullException(nameof(originalSqlColumn));
            _sqlSplitterColumn = sqlSplitterColumn
                ?? throw new ArgumentNullException(nameof(sqlSplitterColumn));
            _sqlGridSplitter = sqlGridSplitter
                ?? throw new ArgumentNullException(nameof(sqlGridSplitter));
            _compareSqlButton = compareSqlButton
                ?? throw new ArgumentNullException(nameof(compareSqlButton));
            _deadlockLeftColumn = deadlockLeftColumn
                ?? throw new ArgumentNullException(nameof(deadlockLeftColumn));
            _deadlockRightColumn = deadlockRightColumn
                ?? throw new ArgumentNullException(nameof(deadlockRightColumn));
            _deadlockLeftButton = deadlockLeftButton
                ?? throw new ArgumentNullException(nameof(deadlockLeftButton));
            _deadlockRightButton = deadlockRightButton
                ?? throw new ArgumentNullException(nameof(deadlockRightButton));
            _planContentGrid = planContentGrid
                ?? throw new ArgumentNullException(nameof(planContentGrid));
        }

        public void ToggleSqlCompare()
        {
            Core.Services.SqlComparePanelLayout layout =
                _layoutService.ToggleSqlCompare(_originalSqlColumn.Width);

            _originalSqlColumn.Width = layout.OriginalSqlWidth;
            _sqlSplitterColumn.Width = layout.SplitterWidth;
            _sqlGridSplitter.Visibility = layout.SplitterVisibility;
            _compareSqlButton.Content = layout.ButtonContent;
        }

        public void ToggleDeadlockLeftPanel()
        {
            Core.Services.SidePanelLayout layout =
                _layoutService.ToggleDeadlockLeftPanel(_deadlockLeftColumn.Width);
            _deadlockLeftColumn.Width = layout.Width;
            _deadlockLeftButton.Content = layout.ButtonContent;
        }

        public void ToggleDeadlockRightPanel()
        {
            Core.Services.SidePanelLayout layout =
                _layoutService.ToggleDeadlockRightPanel(_deadlockRightColumn.Width);
            _deadlockRightColumn.Width = layout.Width;
            _deadlockRightButton.Content = layout.ButtonContent;
        }

        public void ExpandPlanLeftPanel()
        {
            if (_planContentGrid.ColumnDefinitions.Count > 0)
            {
                _planContentGrid.ColumnDefinitions[0].Width =
                    _layoutService.ExpandCollapsiblePanel(_planLeftColumnWidth);
            }
        }

        public void CollapsePlanLeftPanel()
        {
            if (_planContentGrid.ColumnDefinitions.Count > 0)
            {
                Core.Services.CollapsiblePanelLayout layout =
                    _layoutService.CollapseCollapsiblePanel(
                        _planContentGrid.ColumnDefinitions[0].Width);
                _planLeftColumnWidth = layout.StoredWidth;
                _planContentGrid.ColumnDefinitions[0].Width = layout.AppliedWidth;
            }
        }

        public void ExpandPlanRightPanel()
        {
            if (_planContentGrid.ColumnDefinitions.Count > 4)
            {
                _planContentGrid.ColumnDefinitions[4].Width =
                    _layoutService.ExpandCollapsiblePanel(_planRightColumnWidth);
            }
        }

        public void CollapsePlanRightPanel()
        {
            if (_planContentGrid.ColumnDefinitions.Count > 4)
            {
                Core.Services.CollapsiblePanelLayout layout =
                    _layoutService.CollapseCollapsiblePanel(
                        _planContentGrid.ColumnDefinitions[4].Width);
                _planRightColumnWidth = layout.StoredWidth;
                _planContentGrid.ColumnDefinitions[4].Width = layout.AppliedWidth;
            }
        }
    }
}
