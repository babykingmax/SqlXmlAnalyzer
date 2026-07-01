using System.Windows;
using System.Windows.Controls;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views
{
    public partial class PlanWorkspaceView : UserControl
    {
        public PlanWorkspaceView()
        {
            InitializeComponent();
        }

        public Grid ContentGrid => PlanContentGrid;
        public TreeView OperatorTree => PlanOperatorTree;
        public TreeView VisualTree => PlanVisualTree;
        public PlanGraphControl NodifyGraph => PlanNodifyGraph;
        public DataGrid PropertiesGrid => PlanPropertiesGrid;
        public DataGrid RecostDataGrid => RecostGrid;
        public TextBox XmlTextBox => PlanXmlTextBox;
        public TextBox StatementTextBox => PlanStatementTextBox;
        public TextBox WarningsTextBox => PlanWarningsTextBox;
        public TabControl GraphTabControl => PlanGraphTabControl;
        public StatisticsHistogramControl StatisticsHistogram => StatisticsHistogramView;
        public RichTextBox OriginalSqlText => OriginalSqlTextBox;
        public RichTextBox RefactoredSqlText => RefactoredSqlTextBox;
        public ColumnDefinition OriginalSqlColumn => OriginalSqlCol;
        public ColumnDefinition SqlSplitterColumn => SqlSplitterCol;
        public GridSplitter SqlSplitter => SqlGridSplitter;
        public Button CompareSqlButton => BtnCompareSql;

        public event RoutedEventHandler? LeftPanelExpanded;
        public event RoutedEventHandler? LeftPanelCollapsed;
        public event RoutedEventHandler? RightPanelExpanded;
        public event RoutedEventHandler? RightPanelCollapsed;
        public event RoutedPropertyChangedEventHandler<object>? OperatorTreeSelectedItemChanged;
        public event RoutedPropertyChangedEventHandler<object>? VisualTreeSelectedItemChanged;
        public event EventHandler<PlanNodeViewModel?>? GraphNodeSelected;
        public event EventHandler<PlanNodeViewModel?>? GraphNodeDoubleClicked;
        public event RoutedEventHandler? CopyRefactoredSqlClicked;
        public event RoutedEventHandler? CompareSqlClicked;
        public event RoutedEventHandler? CopyIndexDdlClicked;
        public event RoutedEventHandler? CopyDeploymentBundleClicked;
        public event RoutedEventHandler? CopyRollbackDdlClicked;

        private void LeftPanel_Expanded(object sender, RoutedEventArgs e) =>
            LeftPanelExpanded?.Invoke(sender, e);

        private void LeftPanel_Collapsed(object sender, RoutedEventArgs e) =>
            LeftPanelCollapsed?.Invoke(sender, e);

        private void RightPanel_Expanded(object sender, RoutedEventArgs e) =>
            RightPanelExpanded?.Invoke(sender, e);

        private void RightPanel_Collapsed(object sender, RoutedEventArgs e) =>
            RightPanelCollapsed?.Invoke(sender, e);

        private void PlanOperatorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
            OperatorTreeSelectedItemChanged?.Invoke(sender, e);

        private void PlanVisualTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
            VisualTreeSelectedItemChanged?.Invoke(sender, e);

        private void PlanNodifyGraph_NodeSelected(object? sender, PlanNodeViewModel? node) =>
            GraphNodeSelected?.Invoke(sender ?? this, node);

        private void PlanNodifyGraph_NodeDoubleClicked(object? sender, PlanNodeViewModel? node) =>
            GraphNodeDoubleClicked?.Invoke(sender ?? this, node);

        private void CopyRefactoredSql_Click(object sender, RoutedEventArgs e) =>
            CopyRefactoredSqlClicked?.Invoke(sender, e);

        private void CompareSql_Click(object sender, RoutedEventArgs e) =>
            CompareSqlClicked?.Invoke(sender, e);

        private void CopyIndexDdl_Click(object sender, RoutedEventArgs e) =>
            CopyIndexDdlClicked?.Invoke(sender, e);

        private void CopyDeploymentBundle_Click(object sender, RoutedEventArgs e) =>
            CopyDeploymentBundleClicked?.Invoke(sender, e);

        private void CopyRollbackDdl_Click(object sender, RoutedEventArgs e) =>
            CopyRollbackDdlClicked?.Invoke(sender, e);
    }
}
