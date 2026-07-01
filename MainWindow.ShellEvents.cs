using System;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _shellActionService.HandleTitleBarMouseLeftButtonDown(e.ClickCount);
        }

        private void Minimize_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _shellActionService.Minimize();
        }

        private void Maximize_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _shellActionService.ToggleMaximize();
        }

        private void Close_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _shellActionService.Close();
        }

        private void ThemeToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _shellActionService.SetTheme();
        }
    }
}
