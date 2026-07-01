using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views
{
    public partial class MainWorkspaceView : UserControl
    {
        public MainWorkspaceView()
        {
            InitializeComponent();
        }

        public TabControl Tabs => MainTabControl;
        public DeadlockWorkspaceView Deadlock => DeadlockWorkspace;
        public PlanWorkspaceView Plan => PlanWorkspace;
        public PlanComparisonWorkspaceView PlanComparison => PlanComparisonWorkspace;

        public event SelectionChangedEventHandler? DeadlockProcessesSelectionChanged;
        public event SelectionChangedEventHandler? DeadlockResourcesSelectionChanged;
        public event SelectionChangedEventHandler? XelDeadlockSelectionChanged;
        public event RoutedEventHandler? DeadlockToggleLeftClicked;
        public event RoutedEventHandler? DeadlockToggleRightClicked;
        public event RoutedEventHandler? DeadlockZoomToFitClicked;
        public event RoutedEventHandler? DeadlockPlaybackModeChecked;
        public event RoutedEventHandler? DeadlockPlaybackModeUnchecked;
        public event RoutedEventHandler? CopyDeadlockMermaidClicked;
        public event RoutedEventHandler? OpenDeadlockMermaidClicked;
        public event SelectionChangedEventHandler? DeadlockPatternsSelectionChanged;
        public event RoutedEventHandler? PlanLeftPanelExpanded;
        public event RoutedEventHandler? PlanLeftPanelCollapsed;
        public event RoutedEventHandler? PlanRightPanelExpanded;
        public event RoutedEventHandler? PlanRightPanelCollapsed;
        public event RoutedPropertyChangedEventHandler<object>? PlanOperatorTreeSelectedItemChanged;
        public event RoutedPropertyChangedEventHandler<object>? PlanVisualTreeSelectedItemChanged;
        public event EventHandler<PlanNodeViewModel?>? PlanGraphNodeSelected;
        public event EventHandler<PlanNodeViewModel?>? PlanGraphNodeDoubleClicked;
        public event RoutedEventHandler? CopyRefactoredSqlClicked;
        public event RoutedEventHandler? CompareSqlClicked;
        public event RoutedEventHandler? CopyIndexDdlClicked;
        public event RoutedEventHandler? CopyDeploymentBundleClicked;
        public event RoutedEventHandler? CopyRollbackDdlClicked;
        public event MouseButtonEventHandler? TuningHistoryMouseDoubleClicked;
        public event RoutedEventHandler? SaveSessionClicked;
        public event RoutedEventHandler? LoadSessionClicked;
        public event RoutedEventHandler? SwapPlanABClicked;

        private void DeadlockProcessesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            DeadlockProcessesSelectionChanged?.Invoke(sender, e);

        private void DeadlockResourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            DeadlockResourcesSelectionChanged?.Invoke(sender, e);

        private void XelDeadlockSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            XelDeadlockSelectionChanged?.Invoke(sender, e);

        private void ToggleLeft_Click(object sender, RoutedEventArgs e) =>
            DeadlockToggleLeftClicked?.Invoke(sender, e);

        private void ToggleRight_Click(object sender, RoutedEventArgs e) =>
            DeadlockToggleRightClicked?.Invoke(sender, e);

        private void ZoomToFitDeadlock_Click(object sender, RoutedEventArgs e) =>
            DeadlockZoomToFitClicked?.Invoke(sender, e);

        private void PlaybackModeToggle_Checked(object sender, RoutedEventArgs e) =>
            DeadlockPlaybackModeChecked?.Invoke(sender, e);

        private void PlaybackModeToggle_Unchecked(object sender, RoutedEventArgs e) =>
            DeadlockPlaybackModeUnchecked?.Invoke(sender, e);

        private void CopyDeadlockMermaid_Click(object sender, RoutedEventArgs e) =>
            CopyDeadlockMermaidClicked?.Invoke(sender, e);

        private void OpenDeadlockMermaidInBrowser_Click(object sender, RoutedEventArgs e) =>
            OpenDeadlockMermaidClicked?.Invoke(sender, e);

        private void DeadlockPatternsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            DeadlockPatternsSelectionChanged?.Invoke(sender, e);

        private void LeftPanel_Expanded(object sender, RoutedEventArgs e) =>
            PlanLeftPanelExpanded?.Invoke(sender, e);

        private void LeftPanel_Collapsed(object sender, RoutedEventArgs e) =>
            PlanLeftPanelCollapsed?.Invoke(sender, e);

        private void RightPanel_Expanded(object sender, RoutedEventArgs e) =>
            PlanRightPanelExpanded?.Invoke(sender, e);

        private void RightPanel_Collapsed(object sender, RoutedEventArgs e) =>
            PlanRightPanelCollapsed?.Invoke(sender, e);

        private void PlanOperatorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
            PlanOperatorTreeSelectedItemChanged?.Invoke(sender, e);

        private void PlanVisualTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
            PlanVisualTreeSelectedItemChanged?.Invoke(sender, e);

        private void PlanNodifyGraph_NodeSelected(object? sender, PlanNodeViewModel? node) =>
            PlanGraphNodeSelected?.Invoke(sender ?? this, node);

        private void PlanNodifyGraph_NodeDoubleClicked(object? sender, PlanNodeViewModel? node) =>
            PlanGraphNodeDoubleClicked?.Invoke(sender ?? this, node);

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

        private void TuningHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            TuningHistoryMouseDoubleClicked?.Invoke(sender, e);

        private void SaveSession_Click(object sender, RoutedEventArgs e) =>
            SaveSessionClicked?.Invoke(sender, e);

        private void LoadSession_Click(object sender, RoutedEventArgs e) =>
            LoadSessionClicked?.Invoke(sender, e);

        private void SwapPlanAB_Click(object sender, RoutedEventArgs e) =>
            SwapPlanABClicked?.Invoke(sender, e);
    }
}
