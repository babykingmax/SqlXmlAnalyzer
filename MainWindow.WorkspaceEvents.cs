using System.Windows;
using System.Windows.Controls;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
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



    }
}
