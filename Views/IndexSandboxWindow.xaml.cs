using System.Windows;

namespace SqlXmlAnalyzer.Views
{
    public partial class IndexSandboxWindow : Window
    {
        public IndexSandboxWindow()
        {
            InitializeComponent();
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
