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
}
