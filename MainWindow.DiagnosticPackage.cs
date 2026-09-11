using System.Windows;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Views;

namespace SqlXmlAnalyzer;

public partial class MainWindow
{
    internal void ShowDiagnosticPackage()
    {
        try
        {
            DiagnosticReport? report = null;
            string? sourceNotice = null;
            try
            {
                if (MainTabControl.SelectedIndex == 1 && ViewModel.PlanWorkspace.Selection != null)
                    report = ViewModel.PlanWorkspace.CreateReport();
                else if (MainTabControl.SelectedIndex == 0 && ViewModel.DeadlockWorkspace.Analysis != null)
                    report = ViewModel.DeadlockWorkspace.CreateReport(ViewModel.CurrentDeadlockInput?.Envelope);
            }
            catch (Exception exception)
            {
                // A broken/stale report must not prevent collecting a metadata-only diagnostic package.
                sourceNotice = "所选报告快照创建失败；仍可收集元数据。以下详情仅本地显示：" +
                    ExceptionPolicy.Describe(exception, "IMP27.Package.CaptureReport");
            }
            var model = new DiagnosticPackageViewModel(report, _ruleConfiguration.Current, ViewModel.AnalysisOperation.Progress,
                sourceNotice: sourceNotice);
            new DiagnosticPackageWindow(model, _fileDialogService) { Owner = this }.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, ExceptionPolicy.Describe(exception, "IMP27.Package.Open"), "诊断数据包打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
