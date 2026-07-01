using System.Windows;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Views
{
    public partial class ShellNavigationRail : UserControl
    {
        public ShellNavigationRail()
        {
            InitializeComponent();
        }

        public bool IsThemeToggleChecked => ThemeToggle.IsChecked == true;

        public event RoutedEventHandler? OpenDeadlockClicked;
        public event RoutedEventHandler? OpenPlanClicked;
        public event RoutedEventHandler? GenerateHtmlReportClicked;
        public event RoutedEventHandler? ExportWordClicked;
        public event RoutedEventHandler? ExportPdfClicked;
        public event RoutedEventHandler? ExportObfuscatedPlanClicked;
        public event RoutedEventHandler? CopyAnalysisResultClicked;
        public event RoutedEventHandler? ClearResultsClicked;
        public event RoutedEventHandler? ThemeToggled;
        public event RoutedEventHandler? AboutClicked;

        private void OnOpenDeadlockClick(object sender, RoutedEventArgs e) =>
            OpenDeadlockClicked?.Invoke(this, e);

        private void OnOpenPlanClick(object sender, RoutedEventArgs e) =>
            OpenPlanClicked?.Invoke(this, e);

        private void OnGenerateHtmlReportClick(object sender, RoutedEventArgs e) =>
            GenerateHtmlReportClicked?.Invoke(this, e);

        private void OnExportWordClick(object sender, RoutedEventArgs e) =>
            ExportWordClicked?.Invoke(this, e);

        private void OnExportPdfClick(object sender, RoutedEventArgs e) =>
            ExportPdfClicked?.Invoke(this, e);

        private void OnExportObfuscatedPlanClick(object sender, RoutedEventArgs e) =>
            ExportObfuscatedPlanClicked?.Invoke(this, e);

        private void OnCopyAnalysisResultClick(object sender, RoutedEventArgs e) =>
            CopyAnalysisResultClicked?.Invoke(this, e);

        private void OnClearResultsClick(object sender, RoutedEventArgs e) =>
            ClearResultsClicked?.Invoke(this, e);

        private void OnThemeToggleClick(object sender, RoutedEventArgs e) =>
            ThemeToggled?.Invoke(this, e);

        private void OnAboutClick(object sender, RoutedEventArgs e) =>
            AboutClicked?.Invoke(this, e);
    }
}
