using System.Windows;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
        private void ExportObfuscatedPlan_Click(object sender, RoutedEventArgs e)
        {
            ShowDiagnosticReport("sqlplan");
        }

        private void GenerateHtmlReport_Click(object sender, RoutedEventArgs e)
        {
            ShowDiagnosticReport("html");
        }

        private void ExportToPdf_Click(object sender, RoutedEventArgs e)
        {
            ShowDiagnosticReport("pdf");
        }

        private void ExportToWord_Click(object sender, RoutedEventArgs e)
        {
            ShowDiagnosticReport("docx");
        }

        internal void ShowDiagnosticReport(string format)
        {
            try
            {
                var report = format == "sqlplan" || MainTabControl.SelectedIndex == 1
                    ? ViewModel.PlanWorkspace.CreateReport()
                    : MainTabControl.SelectedIndex == 0
                        ? ViewModel.DeadlockWorkspace.CreateReport(ViewModel.CurrentDeadlockInput?.Envelope)
                        : throw new System.IO.InvalidDataException("请在计划或死锁工作区选择要导出的语句/事件。");
                new Views.ReportReviewWindow(new ViewModels.ReportReviewViewModel(report, format), _fileDialogService) { Owner = this }.Show();
            }
            catch (System.Exception exception)
            {
                MessageBox.Show(this, Core.Diagnostics.ExceptionPolicy.Describe(exception, "IMP22.ReportReview.Open"), "报告预览不可用", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
