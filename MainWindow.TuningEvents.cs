using System.Windows;
using System.Windows.Input;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
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


    }
}
