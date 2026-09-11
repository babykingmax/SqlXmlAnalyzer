using System.Windows;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Views;

public partial class RewriteReviewWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    public RewriteReviewWindow(string sourceSql, RewriteReview review)
    {
        InitializeComponent();
        Services.WorkspaceAccessibility.PrepareDialog(this);
        DataContext = new RewriteReviewViewModel(sourceSql, review);
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var source = new Microsoft.Win32.OpenFileDialog { Title = "选择与审核原文一致的 SQL 文件", Filter = "SQL 文件|*.sql|所有文件|*.*" };
            if (source.ShowDialog(this) != true) return;
            var scenarios = new Microsoft.Win32.OpenFileDialog { Title = "选择隔离数据库验证场景", Filter = "语义场景 JSON|*.json" };
            if (scenarios.ShowDialog(this) != true) return;
            await ((RewriteReviewViewModel)DataContext).ValidateAsync(source.FileName,
                SqlSemanticSandboxPolicy.Load(scenarios.FileName), _lifetime.Token);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, ExceptionPolicy.Describe(exception, "RewriteReview.LoadScenarios"), "语义验证失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => ((RewriteReviewViewModel)DataContext).Apply(_lifetime.Token);
    private void CopyCandidate_Click(object sender, RoutedEventArgs e) => ((RewriteReviewViewModel)DataContext).CopyCandidate(Clipboard.SetText);

    private void SaveCandidate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存候选到新文件（不能覆盖已有文件）", Filter = "SQL 文件|*.sql",
                DefaultExt = ".sql", AddExtension = true, FileName = "candidate.sql"
            };
            if (dialog.ShowDialog(this) == true) ((RewriteReviewViewModel)DataContext).SaveCandidateNew(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, ExceptionPolicy.Describe(exception, "IMP21.RewriteReview.SaveDialog"), "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
