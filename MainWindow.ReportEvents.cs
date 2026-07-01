using System.Windows;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
        private void ExportObfuscatedPlan_Click(object sender, RoutedEventArgs e)
        {
            _planObfuscationExportUiActionService.Export(
                ViewModel.CurrentPlanDoc,
                status => ShellStatus.StatusTextBlock.Text = status);
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


    }
}
