using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Views;

namespace SqlXmlAnalyzer.Tests;

public sealed class ReportReviewLayoutAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongReport_KeepsPreviewBoundedAndSaveReachable(bool compact)
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var input = new InputRecognitionService().Parse(InputRecognitionTests.Fixture("imp14_cardinality_residual.sqlplan"));
            var window = new ReportReviewWindow(new ReportReviewViewModel(DiagnosticReportTests.Report(input.Document)))
            {
                Left = -20000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false
            };
            try
            {
                window.Show();
                if (compact) { window.Width = 850; window.Height = 620; }
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var preview = (TextBox)window.FindName("ReportPreview");
                preview.Text.Length.Should().BeGreaterThan(10000);
                preview.ActualHeight.Should().BeLessThan(600, "the entire report must scroll inside a bounded preview");
                preview.ActualWidth.Should().BeLessThanOrEqualTo(1080);
                var scroll = (ScrollViewer)window.Content;
                scroll.ExtentHeight.Should().BeLessThan(800, "report length must not push save thousands of pixels away");
                var save = (Button)window.FindName("SaveReport");
                save.BringIntoView();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Point position = save.TranslatePoint(new Point(), scroll);
                position.X.Should().BeGreaterThanOrEqualTo(0);
                position.Y.Should().BeGreaterThanOrEqualTo(0);
                (position.X + save.ActualWidth).Should().BeLessThanOrEqualTo(scroll.ViewportWidth + 1);
                (position.Y + save.ActualHeight).Should().BeLessThanOrEqualTo(scroll.ViewportHeight + 1);
            }
            finally { window.Close(); }
        });
    }
}
