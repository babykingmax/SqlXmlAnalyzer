using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace SqlXmlAnalyzer.Tests;

public sealed class StatisticsHistogramMockDrawingTests
{
    [Theory]
    [InlineData("1e308", "-1e308")]
    [InlineData("1e308", "bad")]
    [InlineData("NaN", "Infinity")]
    [InlineData("5e-324", "1e-323")]
    [InlineData("1.7976931348623157e308", "1.7976931348623155e308")]
    [InlineData("0", "0")]
    public void MockHistogram_ExtremeOrInvalidParametersKeepEveryCoordinateFinite(string compiled, string runtime) =>
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            StatisticsHistogramHardeningTests.EnsureControlResources();
            var control = new StatisticsHistogramControl();
            control.LoadParameterData("@p", compiled, runtime);
            var canvas = (Canvas)control.FindName("DrawCanvas");
            canvas.Children.Clear();

            Invoke(control, "DrawMockHistogram", 900d, 400d);

            Assert.Equal(20, canvas.Children.OfType<Rectangle>().Count());
            Assert.Equal(7, canvas.Children.OfType<Line>().Count());
            Assert.All(canvas.Children.OfType<Rectangle>(), rectangle =>
            {
                Assert.True(double.IsFinite(rectangle.Width) && rectangle.Width >= 0);
                Assert.True(double.IsFinite(rectangle.Height) && rectangle.Height >= 0);
                Assert.True(double.IsFinite(Canvas.GetLeft(rectangle)) && double.IsFinite(Canvas.GetTop(rectangle)));
            });
            Assert.All(canvas.Children.OfType<Line>(), line =>
                Assert.True(double.IsFinite(line.X1) && double.IsFinite(line.X2) && double.IsFinite(line.Y1) && double.IsFinite(line.Y2)));
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyStringLiteral_IsEstimatedWhileMissingParameterRemainsUnavailable(bool compiledMissing) =>
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            StatisticsHistogramHardeningTests.EnsureControlResources();
            var control = new StatisticsHistogramControl();
            control.LoadParameterData("@p", compiledMissing ? "" : "('')", compiledMissing ? "('')" : "");
            string text = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\n\t0\t77\t0\t0\nA\t0\t1\t0\t0";
            var input = (TextBox)control.FindName("TxtStatsInput");
            input.Text = text;

            Invoke(control, "BtnApplyStats_Click", null, new RoutedEventArgs());

            var compiledEstimate = (TextBlock)control.FindName("TxtCompiledEstimate");
            var runtimeEstimate = (TextBlock)control.FindName("TxtRuntimeEstimate");
            Assert.Contains("N/A", compiledMissing ? compiledEstimate.Text : runtimeEstimate.Text);
            Assert.Contains("77", compiledMissing ? runtimeEstimate.Text : compiledEstimate.Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)control.FindName("PanelSniffingRatio")).Visibility);

            control.ClearSelection();
            input.Text = text;
            Invoke(control, "BtnApplyStats_Click", null, new RoutedEventArgs());
            Assert.Contains("N/A", compiledEstimate.Text);
            Assert.Contains("N/A", runtimeEstimate.Text);
        });

    private static void Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
