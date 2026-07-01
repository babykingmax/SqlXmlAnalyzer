namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
        private void WireMainShellEvents()
        {
            MainShell.TitleBarMouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
            MainShell.MinimizeClicked += Minimize_Click;
            MainShell.MaximizeClicked += Maximize_Click;
            MainShell.CloseClicked += Close_Click;
            MainShell.OpenDeadlockClicked += OpenDeadlockFile_Click;
            MainShell.OpenPlanClicked += OpenPlanFile_Click;
            MainShell.GenerateHtmlReportClicked += GenerateHtmlReport_Click;
            MainShell.ExportWordClicked += ExportToWord_Click;
            MainShell.ExportPdfClicked += ExportToPdf_Click;
            MainShell.ExportObfuscatedPlanClicked += ExportObfuscatedPlan_Click;
            MainShell.CopyAnalysisResultClicked += CopyAnalysisResult_Click;
            MainShell.ClearResultsClicked += ClearResults_Click;
            MainShell.ThemeToggled += ThemeToggle_Click;
            MainShell.AboutClicked += About_Click;
            MainShell.DeadlockProcessesSelectionChanged += DeadlockProcessesList_SelectionChanged;
            MainShell.DeadlockResourcesSelectionChanged += DeadlockResourcesList_SelectionChanged;
            MainShell.XelDeadlockSelectionChanged += XelDeadlockSelector_SelectionChanged;
            MainShell.DeadlockToggleLeftClicked += ToggleLeft_Click;
            MainShell.DeadlockToggleRightClicked += ToggleRight_Click;
            MainShell.DeadlockZoomToFitClicked += ZoomToFitDeadlock_Click;
            MainShell.DeadlockPlaybackModeChecked += PlaybackModeToggle_Checked;
            MainShell.DeadlockPlaybackModeUnchecked += PlaybackModeToggle_Unchecked;
            MainShell.CopyDeadlockMermaidClicked += CopyDeadlockMermaid_Click;
            MainShell.OpenDeadlockMermaidClicked += OpenDeadlockMermaidInBrowser_Click;
            MainShell.DeadlockPatternsSelectionChanged += DeadlockPatternsListBox_SelectionChanged;
            MainShell.PlanLeftPanelExpanded += LeftPanel_Expanded;
            MainShell.PlanLeftPanelCollapsed += LeftPanel_Collapsed;
            MainShell.PlanRightPanelExpanded += RightPanel_Expanded;
            MainShell.PlanRightPanelCollapsed += RightPanel_Collapsed;
            MainShell.PlanOperatorTreeSelectedItemChanged += PlanOperatorTree_SelectedItemChanged;
            MainShell.PlanVisualTreeSelectedItemChanged += PlanVisualTree_SelectedItemChanged;
            MainShell.PlanGraphNodeSelected += OnPlanGraphNodeSelected;
            MainShell.PlanGraphNodeDoubleClicked += OnPlanGraphNodeDoubleClicked;
            MainShell.CopyRefactoredSqlClicked += CopyRefactoredSql_Click;
            MainShell.CompareSqlClicked += CompareSql_Click;
            MainShell.CopyIndexDdlClicked += CopyIndexDdl_Click;
            MainShell.CopyDeploymentBundleClicked += CopyDeploymentBundle_Click;
            MainShell.CopyRollbackDdlClicked += CopyRollbackDdl_Click;
            MainShell.TuningHistoryMouseDoubleClicked += TuningHistoryListView_MouseDoubleClick;
            MainShell.SaveSessionClicked += SaveSession_Click;
            MainShell.LoadSessionClicked += LoadSession_Click;
            MainShell.SwapPlanABClicked += SwapPlanAB_Click;
        }

        private void OnPlanGraphNodeSelected(object? sender, PlanNodeViewModel? node)
        {
            if (node is not null)
            {
                PlanNodifyGraph_NodeSelected(sender ?? MainShell, node);
            }
        }

        private void OnPlanGraphNodeDoubleClicked(object? sender, PlanNodeViewModel? node)
        {
            if (node is not null)
            {
                PlanNodifyGraph_NodeDoubleClicked(sender ?? MainShell, node);
            }
        }
    }
}
