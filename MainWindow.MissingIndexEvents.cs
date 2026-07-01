using System.Windows;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
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

    }
}
