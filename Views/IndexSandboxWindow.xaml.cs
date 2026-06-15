using System.Windows;

namespace SqlXmlAnalyzer.Views
{
    public partial class IndexSandboxWindow : Window
    {
        public IndexSandboxWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CopyAndClose_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.IndexSandboxViewModel vm)
            {
                Clipboard.SetText(vm.CreateIndexStatement);
                MessageBox.Show("脚本已复制到剪贴板！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
        }
    }
}
