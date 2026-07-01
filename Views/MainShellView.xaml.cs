using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views
{
    public partial class MainShellView : UserControl
    {
        public MainShellView()
        {
            InitializeComponent();
        }

        public ShellNavigationRail Navigation => NavigationRail;
        public MainWorkspaceView Workspace => MainWorkspace;
        public ShellStatusBar StatusBar => ShellStatus;

        public event MouseButtonEventHandler? TitleBarMouseLeftButtonDown;
        public event RoutedEventHandler? MinimizeClicked;
        public event RoutedEventHandler? MaximizeClicked;
        public event RoutedEventHandler? CloseClicked;
        public event RoutedEventHandler? OpenDeadlockClicked;
        public event RoutedEventHandler? OpenPlanClicked;
        public event RoutedEventHandler? GenerateHtmlReportClicked;
        public event RoutedEventHandler? ExportWordClicked;
        public event RoutedEventHandler? ExportPdfClicked;
        public event RoutedEventHandler? ExportObfuscatedPlanClicked;
        public event RoutedEventHandler? CopyAnalysisResultClicked;
        public event RoutedEventHandler? ClearResultsClicked;
        public event RoutedEventHandler? ThemeToggled;
        public event RoutedEventHandler? AboutClicked;
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            TitleBarMouseLeftButtonDown?.Invoke(sender, e);

        private void Minimize_Click(object sender, RoutedEventArgs e) =>
            MinimizeClicked?.Invoke(sender, e);

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            MaximizeClicked?.Invoke(sender, e);

        private void Close_Click(object sender, RoutedEventArgs e) =>
            CloseClicked?.Invoke(sender, e);

        private void OpenDeadlockFile_Click(object sender, RoutedEventArgs e) =>
            OpenDeadlockClicked?.Invoke(sender, e);

        private void OpenPlanFile_Click(object sender, RoutedEventArgs e) =>
            OpenPlanClicked?.Invoke(sender, e);

        private void GenerateHtmlReport_Click(object sender, RoutedEventArgs e) =>
            GenerateHtmlReportClicked?.Invoke(sender, e);

        private void ExportToWord_Click(object sender, RoutedEventArgs e) =>
            ExportWordClicked?.Invoke(sender, e);

        private void ExportToPdf_Click(object sender, RoutedEventArgs e) =>
            ExportPdfClicked?.Invoke(sender, e);

        private void ExportObfuscatedPlan_Click(object sender, RoutedEventArgs e) =>
            ExportObfuscatedPlanClicked?.Invoke(sender, e);

        private void CopyAnalysisResult_Click(object sender, RoutedEventArgs e) =>
            CopyAnalysisResultClicked?.Invoke(sender, e);

        private void ClearResults_Click(object sender, RoutedEventArgs e) =>
            ClearResultsClicked?.Invoke(sender, e);

        private void ThemeToggle_Click(object sender, RoutedEventArgs e) =>
            ThemeToggled?.Invoke(sender, e);

        private void About_Click(object sender, RoutedEventArgs e) =>
            AboutClicked?.Invoke(sender, e);

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
