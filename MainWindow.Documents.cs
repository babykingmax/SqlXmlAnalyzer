using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
        private XNamespace _showplanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        private async void OpenDeadlockFile_Click(object sender, RoutedEventArgs e)
        {
            await _fileOpenUiActionService.OpenDeadlockAsync(
                _xelDeadlockUiActionService.AnalyzeXelFileAsync,
                AnalyzeDeadlockFile);
        }

        private async void XelDeadlockSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await _xelDeadlockUiActionService.HandleSelectionChangedAsync();
        }

        private void OpenPlanFile_Click(object sender, RoutedEventArgs e)
        {
            _fileOpenUiActionService.OpenPlan(AnalyzeExecutionPlanFile);
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            _fileOpenUiActionService.HandleDragEnter(e);
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            await _fileOpenUiActionService.HandleDropAsync(
                e,
                _xelDeadlockUiActionService.AnalyzeXelFileAsync,
                AnalyzeDeadlockFile,
                AnalyzeExecutionPlanFile);
        }

        private void AnalyzeDeadlockFile(string filePath)
        {
            _ = AnalyzeFileAsync(filePath);
        }

        private void AnalyzeExecutionPlanFile(string filePath)
        {
            _ = AnalyzeFileAsync(filePath);
        }

        public async void AnalyzeFile(string filePath)
        {
            await AnalyzeFileAsync(filePath);
        }

        public async Task AnalyzeFileAsync(string filePath)
        {
            await _documentAnalysisUiActionService.AnalyzeFileAsync(filePath);
        }

        private async Task AnalyzeDeadlockXmlAsync(string xml, string displayName)
        {
            await _documentAnalysisUiActionService.AnalyzeDeadlockXmlAsync(xml, displayName);
        }

        private async Task AnalyzeDeadlockDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken)
        {
            await _documentAnalysisUiActionService.AnalyzeDeadlockDocumentAsync(
                doc,
                filePath,
                requestId,
                cancellationToken);
        }

        private async Task AnalyzeExecutionPlanDocumentAsync(
            XDocument doc,
            string filePath,
            long requestId,
            CancellationToken cancellationToken)
        {
            await _documentAnalysisUiActionService.AnalyzeExecutionPlanDocumentAsync(
                doc,
                filePath,
                requestId,
                cancellationToken);
        }
    }
}
