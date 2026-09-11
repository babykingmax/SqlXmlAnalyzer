using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanFieldBindingTests
{
    [Theory]
    [InlineData("NodeSelected")]
    [InlineData("NodeDoubleClicked")]
    public void LegacyPlanView_RegistersCompatibleGraphEventHandlers(string eventName)
    {
        var handler = typeof(Views.PlanView).GetMethod("PlanNodifyGraph_" + eventName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        handler.Should().NotBeNull("XAML registers this graph event when the view is constructed");
        // An open delegate validates instance and argument types without creating a global WPF Application.
        Action<Views.PlanView, object?, PlanNodeViewModel?> callback =
            handler!.CreateDelegate<Action<Views.PlanView, object?, PlanNodeViewModel?>>();
        callback.Should().NotBeNull();
        typeof(PlanGraphControl).GetEvent(eventName)!.EventHandlerType.Should().Be(typeof(EventHandler<PlanNodeViewModel>));
    }

    [Theory]
    [InlineData("PlanView.xaml", "100", "1,000")]
    [InlineData("PlanWorkspaceView.xaml", "100", "1,000")]
    [InlineData("PlanView.xaml", "0", "1,000")]
    [InlineData("PlanWorkspaceView.xaml", "0", "1,000")]
    [InlineData("PlanView.xaml", null, "N/A")]
    [InlineData("PlanWorkspaceView.xaml", null, "N/A")]
    public void RealDataGridColumns_RenderSeparateOutputAndReadMetrics(string view, string? rows, string expectedRead)
    {
        RunSta(() =>
        {
            string repository = FindRepository();
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = SafeXmlHelper.LoadSafe(Path.Combine(repository, "Views", view));
            XElement columns = source.Descendants(presentation + "DataGrid")
                .Single(element => (string?)element.Attribute(x + "Name") == "RecostGrid")
                .Element(presentation + "DataGrid.Columns")!;
            // Instantiate the production columns in a real WPF DataGrid and assert generated cell text.
            using var reader = new XElement(presentation + "DataGrid", new XAttribute("xmlns", presentation.NamespaceName),
                new XAttribute("AutoGenerateColumns", "False"),
                new XAttribute("IsReadOnly", "True"), new XElement(columns)).CreateReader();
            var grid = (DataGrid)XamlReader.Load(reader);
            // Optional metric columns remain available through the workspace column chooser.
            foreach (var column in grid.Columns) column.Visibility = Visibility.Visible;
            var relOp = InputFieldMappingTests.RelOp(rows == null ? "" : $"""
                <RunTimeInformation><RunTimeCountersPerThread ActualRows="{rows}" ActualRowsRead="1000"
                  ActualExecutionMode="Batch"/></RunTimeInformation><IndexScan Ordered="true"/>
                """);
            relOp.SetAttributeValue("Parallel", rows == null ? null : "true");
            var node = new PlanGraphNodeUiActionService().CreateNodeFromRelOp(relOp, relOp.Name.Namespace, 10, 1000);
            grid.ItemsSource = new[] { node };
            grid.EnableColumnVirtualization = false;
            grid.Measure(new Size(2100, 180));
            grid.Arrange(new Rect(0, 0, 2100, 180));
            grid.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            bool workspace = view == "PlanWorkspaceView.xaml";
            AssertCell(grid, node, "ActualRows", workspace ? "实际输出总行数" : "实际输出行（行）", rows ?? "N/A");
            AssertCell(grid, node, "ActualRowsRead", workspace ? "实际读取总行数" : "实际读取行（行）", expectedRead);
            AssertCell(grid, node, "ExecutionMode", workspace ? "执行模式" : "执行模式 (ExecMode)", rows == null ? "N/A" : "Batch");
            AssertCell(grid, node, "ParallelDisplay", workspace ? "并行" : "并行 (Parallel)", rows == null ? "N/A" : "True");
            AssertCell(grid, node, "Ordered", workspace ? "有序扫描" : "有序扫描 (Ordered)", rows == null ? "N/A" : "True");
            node.ViewMode = DiagramViewMode.Rows;
            // The input provides totals but no execution count: preserve totals without inventing per-execution metrics.
            node.ActualRowsDisplay.Should().Be(rows ?? "N/A");
            node.PrimaryDisplayValue.Should().Be("实 N/A / 估 100 · N/A");
        });
    }

    private static void AssertCell(DataGrid grid, object row, string path, string header, string expected)
    {
        var column = grid.Columns.OfType<DataGridTextColumn>().Single(column => ((Binding)column.Binding).Path.Path == path);
        column.Header.Should().Be(header);
        var cell = column.GetCellContent(row).Should().BeOfType<TextBlock>().Subject;
        cell.GetBindingExpression(TextBlock.TextProperty)!.Status.Should().Be(BindingStatus.Active);
        cell.Text.Should().Be(expected);
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SqlXmlAnalyzer.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository containing WPF source was not found.");
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("WPF binding test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
