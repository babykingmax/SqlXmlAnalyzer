using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Services;
using MessageBox = System.Windows.MessageBox;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow : Window
    {
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChromeInterop.Attach(this);
        }

        public Core.ViewModels.MainViewModel ViewModel { get; }
        private readonly TemporaryFileManager _temporaryFileManager;
        private readonly Core.Services.AnalysisSessionCoordinator _analysisSessions;
        private readonly Core.Services.BrowserLauncher _browserLauncher;
        private readonly MainWindowShellActionService _shellActionService;
        private readonly Core.Services.DocumentOpenService _documentOpenService;
        private readonly Core.Services.DeadlockDocumentController _deadlockDocumentController;
        private readonly Core.Services.PlanDocumentController _planDocumentController;
        private readonly DocumentRefreshUiActionService _documentRefreshUiActionService;
        private readonly PlanComparisonUiActionService _planComparisonUiActionService;
        private readonly Core.Services.MermaidDiagramService _mermaidDiagramService;
        private readonly MermaidDiagramUiActionService _mermaidDiagramUiActionService;
        private readonly Core.Services.AnalysisReportController _analysisReportController;
        private readonly ReportExportUiActionService _reportExportUiActionService;
        private readonly PlanObfuscationExportUiActionService _planObfuscationExportUiActionService;
        private readonly FileOpenUiActionService _fileOpenUiActionService;
        private readonly XelDeadlockUiActionService _xelDeadlockUiActionService;
        private readonly AnalysisResultsUiActionService _analysisResultsUiActionService;
        private readonly DeadlockAnalysisUiActionService _deadlockAnalysisUiActionService;
        private readonly Core.Services.IFileDialogService _fileDialogService;
        private readonly MissingIndexClipboardUiActionService _missingIndexClipboardUiActionService;
        private readonly DeadlockSelectionUiActionService _deadlockSelectionUiActionService;
        private readonly DeadlockViewportUiActionService _deadlockViewportUiActionService;
        private readonly DeadlockCanvasInteractionBinder _deadlockCanvasInteractionBinder;
        private readonly DeadlockGraphRenderUiActionService _deadlockGraphRenderUiActionService;
        private readonly DeadlockGraphElementUiActionService _deadlockGraphElementUiActionService;
        private readonly DeadlockPlaybackUiActionService _deadlockPlaybackUiActionService;
        private readonly WorkspacePanelUiActionService _workspacePanelUiActionService;
        private readonly TuningSessionUiActionService _tuningSessionUiActionService;
        private readonly PlanAnalysisUiActionService _planAnalysisUiActionService;
        private readonly PlanSelectionUiActionService _planSelectionUiActionService;
        private readonly SqlDiffScrollSyncService _sqlDiffScrollSyncService;
        private readonly SqlDiffUiActionService _sqlDiffUiActionService;
        private readonly SqlQuickFixUiActionService _sqlQuickFixUiActionService;
        private readonly PlanStatisticsUiActionService _planStatisticsUiActionService;
        private readonly DocumentAnalysisUiActionService _documentAnalysisUiActionService;

        private readonly DeadlockGraphUiState _deadlockGraphState = new();

        private void UpdatePlaybackGraphVisibility()
        {
            _deadlockPlaybackUiActionService.UpdateGraphVisibility(
                PlaybackModeToggle.IsChecked == true);
        }

        private void PlaybackModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            _deadlockPlaybackUiActionService.ShowPlayback(
                UpdatePlaybackGraphVisibility);
        }

        private void PlaybackModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _deadlockPlaybackUiActionService.HidePlayback();
        }

        private void RenderDeadlockGraphAndZoom(DeadlockGraph graph)
        {
            _deadlockGraphRenderUiActionService.RenderAndZoomToFit(graph);
        }


        #region 闁稿繑婀圭划顒勫礉閻旇鍘?

        private void ExportObfuscatedPlan_Click(object sender, RoutedEventArgs e)
        {
            _planObfuscationExportUiActionService.Export(
                ViewModel.CurrentPlanDoc,
                status => StatusTextBlock.Text = status);
        }

        private void GenerateHtmlReport_Click(object sender, RoutedEventArgs e)
        {
            _reportExportUiActionService.GenerateHtmlReport();
        }

        private void ExportToPdf_Click(object sender, RoutedEventArgs e)
        {
            _reportExportUiActionService.ExportPdfReport();
        }

        private void ExportToWord_Click(object sender, RoutedEventArgs e)
        {
            _reportExportUiActionService.ExportWordReport();
        }

        private void CopyAnalysisResult_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.CopyAnalysisResult();
        }

        private void CopyRefactoredSql_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.CopyRefactoredSql(_sqlDiffUiActionService.CurrentRefactoredSql);
        }

        private void CompareSql_Click(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ToggleSqlCompare();
        }

        private void ClearResults_Click(object sender, RoutedEventArgs e)
        {
            _analysisResultsUiActionService.ClearResults();
        }
        private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.OpenLogsFolder();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.ShowAboutAndRegisterAssociations();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _shellActionService.ExitApplication();
        }

        #endregion

        #region 濞存粌顑勫▎銏″緞閸曨厽鍊?(閻炴稏鍎遍崢?

        private void DeadlockProcessesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _deadlockSelectionUiActionService.SelectCurrentProcess();
        }

        private void DeadlockResourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _deadlockSelectionUiActionService.SelectCurrentResource();
        }

        private void ToggleLeft_Click(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ToggleDeadlockLeftPanel();
        }

        private void ToggleRight_Click(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ToggleDeadlockRightPanel();
        }

        private void ZoomToFitDeadlock_Click(object sender, RoutedEventArgs e)
        {
            _deadlockViewportUiActionService.ZoomToFit();
        }

        private void DeadlockPatternsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _deadlockSelectionUiActionService.SelectCurrentPattern();
        }

        #region 闁硅埖锚瑜版棃妫冮姀鈩冪凡濞存粌顑勫▎銏″緞閸曨厽鍊?
        private void LeftPanel_Expanded(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ExpandPlanLeftPanel();
        }

        private void LeftPanel_Collapsed(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.CollapsePlanLeftPanel();
        }

        private void RightPanel_Expanded(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.ExpandPlanRightPanel();
        }

        private void RightPanel_Collapsed(object sender, RoutedEventArgs e)
        {
            _workspacePanelUiActionService.CollapsePlanRightPanel();
        }
        #endregion


        private void PlanOperatorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _planSelectionUiActionService.SelectCurrentOperatorTreeItem();
        }

        private void RefreshDeadlockGraph_Click(object sender, RoutedEventArgs e)
        {
            _documentRefreshUiActionService.RefreshDeadlockGraph();
        }

        private void CopyDeadlockMermaid_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.CopyDeadlockDiagram();
        }

        private void RefreshPlanGraph_Click(object sender, RoutedEventArgs e)
        {
            _documentRefreshUiActionService.RefreshPlanGraph();
        }

        private void CopyPlanMermaid_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.CopyPlanDiagram();
        }

        private void PlanVisualTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _planSelectionUiActionService.SelectCurrentVisualTreeNode();
        }

        // Nodify 闁煎搫鍊婚崑锝夋焻婢跺鍘?-> 闁告艾鏈鐐哄礆妫颁礁鐦滈柛娆忓帠閺呭墎浠﹂悙绮瑰亾瑜斿浼村级?(Plan Explorer 濡炲瀛╅悧?
        private void PlanNodifyGraph_NodeSelected(object sender, PlanNodeViewModel node)
        {
            _planSelectionUiActionService.SelectFromGraphNode(node);
        }

        private void PlanNodifyGraph_NodeDoubleClicked(object sender, PlanNodeViewModel node)
        {
            _planSelectionUiActionService.SelectFromGraphNode(node);
        }

        private void OpenPlanMermaidInBrowser_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.OpenPlanDiagram();
        }

        private void OpenDeadlockMermaidInBrowser_Click(object sender, RoutedEventArgs e)
        {
            _mermaidDiagramUiActionService.OpenDeadlockDiagram();
        }

        #endregion


        // --- 閻犲鍟╃槐顓㈠储閸℃钑夊☉?A/B 妤犵偠鍩栫敮鎾垛偓浣冾潐閻︻喗绂嶇€ｂ晜顐藉璺哄閹﹪宕?---
        private async void TuningHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await _tuningSessionUiActionService.OpenSelectedHistorySnapshotAsync(
                _analysisSessions,
                AnalyzeExecutionPlanDocumentAsync);
        }

        private void SaveSession_Click(object sender, RoutedEventArgs e)
        {
            _tuningSessionUiActionService.SaveSession();
        }

        private void LoadSession_Click(object sender, RoutedEventArgs e)
        {
            _tuningSessionUiActionService.LoadSession();
        }

        private void SwapPlanAB_Click(object sender, RoutedEventArgs e)
        {
            _tuningSessionUiActionService.SwapPlans();
        }


        #region 闁告瑯鍨甸～瀣礌閺嶎偅绠欓柡澶娿仒缁楀本绂嶉妶鍕瀺閻忕偞娲滈妵?(GUI Dashboard Integration & Interactive Visualization)

        private void CopyIndexDdl_Click(object sender, RoutedEventArgs e)
        {
            _missingIndexClipboardUiActionService.CopyCreateScript(sender);
        }

        private void CopyRollbackDdl_Click(object sender, RoutedEventArgs e)
        {
            _missingIndexClipboardUiActionService.CopyRollbackScript(sender);
        }

        private void CopyDeploymentBundle_Click(object sender, RoutedEventArgs e)
        {
            _missingIndexClipboardUiActionService.CopyDeploymentBundle(sender);
        }

        #endregion
    }
}
