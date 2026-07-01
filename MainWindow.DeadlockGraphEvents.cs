using System.Windows;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
        private void UpdatePlaybackGraphVisibility()
        {
            _deadlockPlaybackUiActionService.UpdateGraphVisibility(
                DeadlockWorkspace.PlaybackModeToggleButton.IsChecked == true);
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


    }
}
