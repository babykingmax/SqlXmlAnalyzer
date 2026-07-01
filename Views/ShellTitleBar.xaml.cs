using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SqlXmlAnalyzer.Views
{
    public partial class ShellTitleBar : UserControl
    {
        public ShellTitleBar()
        {
            InitializeComponent();
        }

        public event MouseButtonEventHandler? TitleBarMouseLeftButtonDown;

        public event RoutedEventHandler? MinimizeClicked;

        public event RoutedEventHandler? MaximizeClicked;

        public event RoutedEventHandler? CloseClicked;

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TitleBarMouseLeftButtonDown?.Invoke(this, e);
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            MinimizeClicked?.Invoke(this, e);
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            MaximizeClicked?.Invoke(this, e);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            CloseClicked?.Invoke(this, e);
        }
    }
}
