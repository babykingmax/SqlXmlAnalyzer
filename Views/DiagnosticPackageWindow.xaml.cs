using System.Windows;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views;

public partial class DiagnosticPackageWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IFileDialogService _dialogs;
    private DiagnosticPackageViewModel Model => (DiagnosticPackageViewModel)DataContext;
    public DiagnosticPackageWindow(DiagnosticPackageViewModel model, IFileDialogService? dialogs = null)
    {
        _dialogs = dialogs ?? new WpfFileDialogService();
        InitializeComponent(); DataContext = model;
        Services.WorkspaceAccessibility.PrepareDialog(this);
        Closed += (_, _) => _cancellation.Cancel();
    }
    private async void Prepare_Click(object sender, RoutedEventArgs e) => await Model.PrepareAsync(_cancellation.Token);
    private void SelectLog_Click(object sender, RoutedEventArgs e) => SelectAttachment(false);
    private void SelectDump_Click(object sender, RoutedEventArgs e) => SelectAttachment(true);
    private void SelectAttachment(bool dump)
    {
        try
        {
            string? path = _dialogs.ShowOpenFile(new(dump ? "Windows minidump (*.dmp)|*.dmp" : "UTF-8 日志 (*.log)|*.log",
                dump ? "选择 DUMP（原样附带，未脱敏）" : "选择日志（原样附带，未脱敏）"));
            if (path != null) { if (dump) Model.DumpPath = path; else Model.LogPath = path; }
        }
        catch (Exception exception) { ShowFailure(exception); }
    }
    private void RemoveLog_Click(object sender, RoutedEventArgs e) => Model.LogPath = null;
    private void RemoveDump_Click(object sender, RoutedEventArgs e) => Model.DumpPath = null;
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Model.CanSave) return;
            string? path = _dialogs.ShowSaveFile(new("诊断数据包 (*.zip)|*.zip", "保存已审核的本地诊断包（新文件）", ".zip", "DiagnosticPackage.zip"));
            if (path != null) await Model.SaveAsync(path, _cancellation.Token);
        }
        catch (Exception exception) { ShowFailure(exception); }
    }
    private void ShowFailure(Exception exception) => MessageBox.Show(this,
        ExceptionPolicy.Describe(exception, "IMP27.Package.Dialog"), "诊断数据包操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
