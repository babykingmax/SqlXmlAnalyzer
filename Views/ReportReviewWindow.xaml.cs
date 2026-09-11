using System.Windows;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views;

public partial class ReportReviewWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IFileDialogService _dialogs;
    public ReportReviewWindow(ReportReviewViewModel model, IFileDialogService? dialogs = null)
    {
        _dialogs = dialogs ?? new WpfFileDialogService();
        InitializeComponent(); DataContext = model; Services.WorkspaceAccessibility.PrepareDialog(this);
        Closed += (_, _) => _cancellation.Cancel();
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var model = (ReportReviewViewModel)DataContext;
        try
        {
            string? path = _dialogs.ShowSaveFile(new($"{model.Format} (*.{model.Format})|*.{model.Format}", "导出诊断报告", "." + model.Format, "DiagnosticReport." + model.Format));
            if (path != null) await model.ExportAsync(path, _cancellation.Token);
        }
        catch (Exception exception) { MessageBox.Show(this, ExceptionPolicy.Describe(exception, "IMP22.ReportReview.Save"), "报告导出失败"); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { _cancellation.Cancel(); Close(); }
}
