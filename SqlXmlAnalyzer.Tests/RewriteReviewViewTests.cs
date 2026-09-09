using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using SqlXmlAnalyzer.Tests.Refactoring;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Views;

namespace SqlXmlAnalyzer.Tests;

public sealed class RewriteReviewViewTests
{
    [Fact]
    public void Window_BindsOriginalAndSelectedPreviewAndLaysOutReviewDetails()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previousMode = RenderOptions.ProcessRenderMode;
            try
            {
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                var result = RewriteProposalTests.Run();
                var window = new RewriteReviewWindow(RewriteProposalTests.Sql, result.Review!);
                var model = (RewriteReviewViewModel)window.DataContext;
                model.PreviewSql.Should().Be(RewriteProposalTests.Sql);
                LayoutAndAssert(window);
                model.Items[0].IsSelected = true;
                model.PreviewSql.Should().Be(result.OutputSql);
                model.Details.Should().Contain("前提").And.Contain("风险").And.Contain("SQL diff");
                LayoutAndAssert(window);
                model.Items[0].IsSelected = false;
                model.PreviewSql.Should().Be(RewriteProposalTests.Sql);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
            finally { RenderOptions.ProcessRenderMode = previousMode; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void LayoutAndAssert(Window window)
    {
        var content = (FrameworkElement)window.Content;
        // Exercise production bindings/layout independently of native screenshot availability.
        window.Content = null;
        content.DataContext = window.DataContext;
        ((System.Windows.Controls.Grid)content).Background = window.Background;
        content.Measure(new Size(1120, 740));
        content.Arrange(new Rect(0, 0, 1120, 740));
        content.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        content.ActualWidth.Should().BeGreaterThan(1000);
        content.ActualHeight.Should().BeGreaterThan(600);
        window.Content = content;
        var model = (RewriteReviewViewModel)window.DataContext;
        ((System.Windows.Controls.TextBox)window.FindName("ReviewOriginalSql")).Text.Should().Be(model.OriginalSql);
        ((System.Windows.Controls.TextBox)window.FindName("ReviewPreviewSql")).Text.Should().Be(model.PreviewSql);
        ((System.Windows.Controls.TextBlock)window.FindName("ReviewApplyStatus")).Text.Should().Be(model.ApplyStatusText);
        ((System.Windows.Controls.TextBlock)window.FindName("ReviewPreviewTitle")).Text.Should().Be(model.PreviewTitle);
    }
}
