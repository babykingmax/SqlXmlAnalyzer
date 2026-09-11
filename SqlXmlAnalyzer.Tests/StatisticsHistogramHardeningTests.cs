using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Xml.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Tests;

public sealed class StatisticsHistogramHardeningTests
{
    private const string Header = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\n";

    [Theory]
    [InlineData("9007199254740992", "9007199254740993")]
    [InlineData("99999999999999999999999999999999999998", "99999999999999999999999999999999999999")]
    [InlineData("0.00000000000000000000000000000000000001", "0.00000000000000000000000000000000000002")]
    [InlineData("-9007199254740993", "-9007199254740992")]
    [InlineData("1e308", "1.0000000000000001e308")]
    public void NumericBoundaries_RemainDistinctBeyondDoubleAndDecimalPrecision(string first, string second)
    {
        var steps = Parse(first + "\t0\t1\t0\t0\n" + second + "\t10\t99999\t1\t10", out var type);
        StatisticsHistogramParser.EstimateValue(second, steps, type, out var rows, out var position, out var match);
        Assert.Equal(99999, rows);
        Assert.Contains("精确匹配", match);
        Assert.True(double.IsFinite(position));
        Assert.True(steps[0].RangeHiKeyNumeric < steps[1].RangeHiKeyNumeric);
    }

    [Fact]
    public void NumericComparison_HandlesEquivalentNotationAndPreciseIntervals()
    {
        var steps = Parse("9007199254740992\t0\t1\t0\t0\n9007199254740994\t20\t99\t2\t10", out var type);
        StatisticsHistogramParser.EstimateValue("9007199254740993", steps, type, out var rows, out _, out var match);
        Assert.Equal(10, rows);
        Assert.Contains("落入区间", match);
        StatisticsHistogramParser.EstimateValue("9.007199254740994e15", steps, type, out rows, out _, out match);
        Assert.Equal(99, rows);
        Assert.Contains("精确匹配", match);
    }

    [Fact]
    public void NullableNumericHistogram_InfersNonNullTypeAndPreservesInterval()
    {
        var steps = Parse("NULL\t0\t500\t0\t0\n1\t0\t1\t0\t0\n2\t0\t2\t0\t0\n10\t800\t3\t8\t100\n20\t0\t4\t0\t0", out var type);
        Assert.Equal(HistogramKeyType.Numeric, type);
        Assert.True(steps[0].IsNull);
        StatisticsHistogramParser.EstimateValue("3", steps, type, out var rows, out _, out _);
        Assert.Equal(100, rows);
    }

    [Fact]
    public void NullableDateTimeHistogram_PreservesAdjacentDatetime2Ticks()
    {
        var steps = Parse("NULL\t0\t5\t0\t0\n2026-01-01 00:00:00.0000001\t0\t1\t0\t0\n2026-01-01 00:00:00.0000002\t0\t99\t0\t0", out var type);
        Assert.Equal(HistogramKeyType.DateTime, type);
        StatisticsHistogramParser.EstimateValue("2026-01-01 00:00:00.0000002", steps, type, out var rows, out _, out _);
        Assert.Equal(99, rows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NULL")]
    [InlineData("not-a-number")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999999999")]
    [InlineData("$0")]
    public void InvalidNumericParameter_DoesNotMatchZero(string value)
    {
        var steps = Parse("0\t0\t1234\t0\t0\n10\t10\t1\t2\t5", out var type);
        StatisticsHistogramParser.EstimateValue(value, steps, type, out var rows, out var position, out var match);
        Assert.True(double.IsNaN(rows));
        Assert.True(double.IsNaN(position));
        Assert.Contains("未知", match);
    }

    [Fact]
    public void InvalidDateTimeParameter_DoesNotFallbackToNumericTicks()
    {
        var steps = Parse("2026-01-01\t0\t1\t0\t0", out var type);
        StatisticsHistogramParser.EstimateValue("0", steps, type, out var rows, out _, out _);
        Assert.True(double.IsNaN(rows));
    }

    [Fact]
    public void StringMatching_DoesNotMergeCaseOrInventCollationOrder()
    {
        var steps = Parse("A\t0\t1\t0\t0\na\t10\t99999\t1\t10", out var type);
        StatisticsHistogramParser.EstimateValue("a", steps, type, out var rows, out _, out _);
        Assert.Equal(99999, rows);
        StatisticsHistogramParser.EstimateValue("B", steps, type, out rows, out var position, out var match);
        Assert.True(double.IsNaN(rows));
        Assert.True(double.IsNaN(position));
        Assert.Contains("排序规则", match);
    }

    [Theory]
    [InlineData(" A")]
    [InlineData("")]
    [InlineData(" ")]
    public void StringKeys_PreserveWhitespaceAndEmptyBoundaries(string first)
    {
        var steps = Parse(first + "\t0\t1\t0\t0\nA\t10\t99999\t1\t10", out var type);
        StatisticsHistogramParser.EstimateValue("A", steps, type, out var rows, out _, out _);
        Assert.Equal(99999, rows);
        StatisticsHistogramParser.EstimateValue(first, steps, type, out rows, out _, out _);
        Assert.Equal(1, rows);
    }

    [Fact]
    public void LowercaseNullString_IsNotTheSsmsNullMarker()
    {
        var steps = Parse("NULL\t0\t500\t0\t0\nnull\t0\t17\t0\t0\nz\t5\t3\t1\t5", out var type);
        StatisticsHistogramParser.EstimateValue("null", steps, type, out var rows, out _, out _);
        Assert.Equal(17, rows);
        StatisticsHistogramParser.EstimateValue("NULL", steps, type, out rows, out _, out var match);
        Assert.True(double.IsNaN(rows));
        Assert.Contains("未知", match);
    }

    [Theory]
    [InlineData("RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\n10\t5\t7")]
    [InlineData("EQ_ROWS\tRANGE_HI_KEY\tRANGE_ROWS\n7\t10\t5")]
    [InlineData("10\t5\t7")]
    public void OptionalColumnsMayBeMissing_WithoutNegativeIndexOrFabricatedAverage(string input)
    {
        var steps = StatisticsHistogramParser.Parse(input, out var type)!;
        Assert.Single(steps);
        Assert.Equal(7, steps[0].EqRows);
        StatisticsHistogramParser.EstimateValue("5", steps, type, out var rows, out _, out var match);
        Assert.True(double.IsNaN(rows));
        Assert.Contains("缺少", match);
    }

    [Theory]
    [InlineData("RANGE_ROWS\tEQ_ROWS\n1\t2")]
    [InlineData("RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\n10\tNaN\t1")]
    [InlineData("RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\n10\t1\t-1")]
    [InlineData("RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\n10\t1")]
    public void MalformedRequiredColumns_AreRejected(string input) => Assert.Null(StatisticsHistogramParser.Parse(input, out _));

    [Fact]
    public void Control_UnknownParameterShowsUnavailableAndDoesNotDrawItsMarker() => PlanIdentityHardeningTests.RunOnStaThread(() =>
    {
        EnsureControlResources();
        var control = new StatisticsHistogramControl();
        control.LoadParameterData("@p", "bad", "(0)");
        ((TextBox)control.FindName("TxtStatsInput")).Text = Header + "NULL\t0\t3\t0\t0\n0\t0\t1234\t0\t0\n10\t10\t1\t2\t5";
        control.Measure(new Size(900, 700));
        control.Arrange(new Rect(0, 0, 900, 700));
        Invoke(control, "BtnApplyStats_Click", null, new RoutedEventArgs());
        Assert.Contains("N/A", ((TextBlock)control.FindName("TxtCompiledEstimate")).Text);
        Assert.DoesNotContain("NaN", ((TextBlock)control.FindName("TxtCompiledEstimate")).Text);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)control.FindName("PanelSniffingRatio")).Visibility);
        var canvas = (Canvas)control.FindName("DrawCanvas");
        Assert.DoesNotContain(canvas.Children.OfType<TextBlock>(), t => t.Text.StartsWith("Compiled:"));
        Assert.Contains(canvas.Children.OfType<TextBlock>(), t => t.Text.StartsWith("Runtime:"));
        Assert.All(canvas.Children.OfType<Line>(), l => Assert.True(double.IsFinite(l.X1) && double.IsFinite(l.Y1)));
    });

    [Fact]
    public void Control_DbccTemplateContainsOnlySafelyQuotedStatements() => PlanIdentityHardeningTests.RunOnStaThread(() =>
    {
        EnsureControlResources();
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        var statistics = new XElement(ns + "StatisticsInfo", new XAttribute("Database", "[D'b]"),
            new XAttribute("Schema", "[dbo]"), new XAttribute("Table", "[T]]able]"),
            new XAttribute("Statistics", "s'); SELECT 123 AS injected;--"));
        var document = new XDocument(new XElement(ns + "QueryPlan", new XElement(ns + "OptimizerStatsUsage", statistics)));
        var control = new StatisticsHistogramControl();
        control.LoadStatisticsUsage(document, ns);
        string sql = ((TextBox)control.FindName("TxtStatsInput")).Text;
        var parser = new TSql160Parser(true);
        var script = (TSqlScript)parser.Parse(new System.IO.StringReader(sql), out var errors);
        Assert.Empty(errors);
        Assert.IsType<DbccStatement>(Assert.Single(script.Batches.SelectMany(b => b.Statements)));
        statistics.SetAttributeValue("Statistics", "[s]'); SELECT 123 AS injected;--");
        control.LoadStatisticsUsage(document, ns);
        sql = ((TextBox)control.FindName("TxtStatsInput")).Text;
        Assert.Contains("未生成命令", sql);
        Assert.DoesNotContain("SELECT 123", sql);
    });

    private static List<HistogramStep> Parse(string data, out HistogramKeyType type) => StatisticsHistogramParser.Parse(Header + data, out type)!;
    internal static void EnsureControlResources()
    {
        // The tests exercise real control state; application themes are tested separately.
        if (System.Windows.Application.Current != null) return;
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (string key in new[] { "MaterialDesignFlatButton", "MaterialDesignRaisedButton", "MaterialDesignOutlinedButton", "SecondaryButtonStyle" })
        {
            var style = new Style(typeof(Button));
            style.Seal();
            application.Resources[key] = style;
        }
    }
    private static void Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
