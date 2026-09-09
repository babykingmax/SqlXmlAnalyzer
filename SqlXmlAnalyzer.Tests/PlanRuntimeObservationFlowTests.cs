using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanRuntimeObservationFlowTests
{
    [Theory]
    [InlineData("", "N/A", 1)]
    [InlineData("<RunTimeInformation/>", "N/A", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread/></RunTimeInformation>", "N/A", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='NaN'/></RunTimeInformation>", "N/A", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='-1'/></RunTimeInformation>", "N/A", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='1.5'/></RunTimeInformation>", "N/A", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='18446744073709551616'/></RunTimeInformation>", "N/A", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='40'/><RunTimeCountersPerThread/></RunTimeInformation>", "N/A", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='40'/><RunTimeCountersPerThread ActualRows='bad'/></RunTimeInformation>", "N/A", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='0'/></RunTimeInformation>", "0", 0)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='250'/></RunTimeInformation>", "250", 2.5)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='40'/><RunTimeCountersPerThread ActualRows='60'/></RunTimeInformation>", "100", 1)]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='250' ActualRowsRead='bad'/></RunTimeInformation>", "250", 2.5)]
    public void Load_UsesCompleteActualOutputOrFallsBackToOwnCost(
        string runtime, string expectedRows, double expectedRecost)
    {
        PlanNodeViewModel node = Load(CreatePlan(runtime)).MasterNodes.Single();

        node.ActualRows.Should().Be(expectedRows);
        node.HasActualRows.Should().Be(expectedRows != "N/A");
        node.OwnCost.Should().Be(1);
        node.ActualRecost.Should().Be(expectedRecost);
        node.PrimaryDisplayValue.Should().Be(expectedRows == "N/A" ? "Est R: 100" : $"R: {expectedRows}");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("N/A", false)]
    [InlineData("未知", false)]
    [InlineData("", true)]
    [InlineData("N/A", true)]
    [InlineData("未知", true)]
    public void Recalculate_DoesNotUsePresentationTextAsRuntimeEvidence(string display, bool observed)
    {
        XDocument document = CreatePlan(observed
            ? "<RunTimeInformation><RunTimeCountersPerThread ActualRows='250'/></RunTimeInformation>"
            : "");
        PlanNodeViewModel node = Load(document).MasterNodes.Single();
        XElement relOp = node.RawElement!;
        node.ActualRows = display;

        new PlanGraphCostUiActionService().ApplyCostCalculations(
            [relOp], new Dictionary<XElement, PlanNodeViewModel> { [relOp] = node },
            relOp.Name.Namespace, DiagramViewMode.Rows, PlanColorMode.TotalCost);

        node.ActualRecost.Should().Be(observed ? 2.5 : 1);
        var connection = new ConnectionViewModel { Source = node };
        connection.RowsCount.Should().Be(observed ? 250 : 100);
        if (observed) connection.ToolTipText.Should().Contain("实际行数: 250");
        else connection.ToolTipText.Should().NotContain("实际行数");
    }

    [Theory]
    [InlineData("", 0, false)]
    [InlineData("0", 0, true)]
    [InlineData("250", 2.5, true)]
    public void Load_ChildRuntimeDoesNotLeakIntoEstimatedParent(string childRows, double childRecost, bool observed)
    {
        XDocument document = CreatePlan(childRows.Length == 0 ? "" :
            $"<RunTimeInformation><RunTimeCountersPerThread ActualRows='{childRows}'/></RunTimeInformation>");
        XNamespace ns = document.Root!.Name.Namespace;
        XElement child = document.Descendants(ns + "RelOp").Single();
        child.SetAttributeValue("NodeId", "1");
        var parent = new XElement(ns + "RelOp", new XAttribute("NodeId", "0"),
            new XAttribute("PhysicalOp", "Filter"), new XAttribute("LogicalOp", "Filter"),
            new XAttribute("EstimateRows", "100"), new XAttribute("EstimatedTotalSubtreeCost", "10"));
        child.ReplaceWith(parent);
        parent.Add(new XElement(ns + "Filter", child));

        PlanGraphLoadUiActionResult result = Load(document);
        PlanNodeViewModel parentNode = result.MasterNodes.Single(n => n.NodeId == "0");
        PlanNodeViewModel childNode = result.MasterNodes.Single(n => n.NodeId == "1");
        ConnectionViewModel connection = result.MasterConnections.Single();

        parentNode.ActualRows.Should().Be("N/A");
        parentNode.HasActualRows.Should().BeFalse();
        childNode.HasActualRows.Should().Be(observed);
        parentNode.OwnCost.Should().Be(9);
        parentNode.ActualRecost.Should().Be(9);
        childNode.ActualRecost.Should().Be(observed ? childRecost : 1);
        connection.RowsCount.Should().Be(observed ? childNode.ActualRowsNum : 100);
        connection.DataSizeVal.Should().Be(connection.RowsCount * 8);
        if (observed) connection.ToolTipText.Should().Contain($"实际行数: {childRows}");
        else connection.ToolTipText.Should().NotContain("实际行数");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='1000'/><RunTimeCountersPerThread/></RunTimeInformation>")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='1000'/><RunTimeCountersPerThread ActualRows='bad'/></RunTimeInformation>")]
    public void Load_UnknownTotalDoesNotExposePartialCountsAsRuntimeMetrics(string runtime)
    {
        PlanNodeViewModel node = Load(CreatePlan(runtime)).MasterNodes.Single();
        var connection = new ConnectionViewModel { Source = node };

        connection.RowsCount.Should().Be(100);
        connection.DataSizeVal.Should().Be(800);
        connection.ToolTipText.Should().NotContain("实际行数").And.NotContain("估算偏差");
        var missingConnection = new ConnectionViewModel { Source = new PlanNodeViewModel() };
        connection.StrokeBrush.Should().BeSameAs(missingConnection.StrokeBrush);
        node.ActualRowsBrush.Should().BeSameAs(Brushes.DimGray);
        node.SkewWarning.Should().BeEmpty();
    }

    private static XDocument CreatePlan(string runtime)
    {
        XDocument document = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("imp07_field_mapping.sqlplan"));
        XNamespace ns = document.Root!.Name.Namespace;
        XElement relOp = document.Descendants(ns + "RelOp").Single();
        relOp.Elements(ns + "RunTimeInformation").Remove();
        if (runtime.Length > 0)
            relOp.Add(SafeXmlHelper.ParseSafe($"<Root xmlns='{ns}'>{runtime}</Root>").Root!.Elements());
        return document;
    }

    private static PlanGraphLoadUiActionResult Load(XDocument document) =>
        new PlanGraphLoadUiActionService().Load(document, document.Root!.Name.Namespace,
            new ObservableCollection<PlanNodeViewModel>(), new ObservableCollection<ConnectionViewModel>(),
            new PlanGraphLoadUiActionOptions
            {
                InitialLayout = PlanLayoutMode.Horizontal,
                InitialColor = PlanColorMode.TotalCost,
                InitialView = DiagramViewMode.Rows,
                InitialLinkMetric = LinkMetricMode.RowCount,
                ResidualIoThreshold = 10,
                ResidualIoMinRowsRead = 1000
            });
}
