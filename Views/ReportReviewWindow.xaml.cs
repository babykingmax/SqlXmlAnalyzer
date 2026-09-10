using System.Windows;
using Microsoft.Win32;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views;

public partial class ReportReviewWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    public ReportReviewWindow(ReportReviewViewModel model) { InitializeComponent(); DataContext = model; Closed += (_, _) => _cancellation.Cancel(); }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var model = (ReportReviewViewModel)DataContext;
        var dialog = new SaveFileDialog { Filter = $"{model.Format} (*.{model.Format})|*.{model.Format}", DefaultExt = "." + model.Format, FileName = "DiagnosticReport." + model.Format, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try { await model.ExportAsync(dialog.FileName, _cancellation.Token); }
        catch (Exception exception) { MessageBox.Show(this, ExceptionPolicy.Describe(exception, "IMP22.ReportReview.Save"), "报告导出失败"); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { _cancellation.Cancel(); Close(); }
}
