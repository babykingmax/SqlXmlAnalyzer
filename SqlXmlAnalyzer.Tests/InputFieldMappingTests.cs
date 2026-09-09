using System.Globalization;
using System.Xml.Schema;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class InputFieldMappingTests
{
    private const string Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    [Fact]
    public void RepresentativePlan_ValidatesAgainstMicrosoftShowplanSchemaAndAgreesWithDetails()
    {
        var document = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("imp07_field_mapping.sqlplan"));
        var schemas = new XmlSchemaSet { XmlResolver = null };
        using var reader = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("showplanxml-sql2019.xsd")).CreateReader();
        schemas.Add(Ns, reader);
        document.Validate(schemas, null);
        XElement relOp = document.Descendants(XName.Get("RelOp", Ns)).Single();
        var node = Build(relOp);
        node.ExecutionMode.Should().Be("Batch");
        node.IsParallel.Should().BeTrue();
        node.Ordered.Should().Be("True");
        var properties = new PlanPropertyService().BuildProperties(relOp);
        properties.Should().Contain(new PlanPropertyItem("Runtime", "实际输出行（行）", node.ActualRows));
        properties.Should().Contain(new PlanPropertyItem("Runtime", "实际读取行（行）", node.ActualRowsRead));
        properties.Should().Contain(new PlanPropertyItem("Runtime", "实际执行模式", "Batch"));
        properties.Should().Contain(new PlanPropertyItem("Estimates", "估算执行模式", "Row"));
        properties.Should().Contain(new PlanPropertyItem("Operator", "并行 (Parallel)", "True"));
        properties.Should().Contain(new PlanPropertyItem("Operator", "有序扫描 (Ordered)", "True"));
    }

    [Theory]
    [InlineData("", "N/A", "N/A")]
    [InlineData("<RunTimeInformation/>", "N/A", "N/A")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread/></RunTimeInformation>", "N/A", "N/A")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='0' ActualRowsRead='1000'/></RunTimeInformation>", "0", "1,000")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='100'/></RunTimeInformation>", "100", "N/A")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRowsRead='1000'/></RunTimeInformation>", "N/A", "1,000")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='100' ActualRowsRead='1000'/><RunTimeCountersPerThread ActualRows='20'/></RunTimeInformation>", "120", "N/A")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='100' ActualRowsRead='1000'/><RunTimeCountersPerThread ActualRows='20' ActualRowsRead='200'/></RunTimeInformation>", "120", "1,200")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='NaN' ActualRowsRead='-1'/></RunTimeInformation>", "N/A", "N/A")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='Infinity' ActualRowsRead='1.5'/></RunTimeInformation>", "N/A", "N/A")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='1000000000000' ActualRowsRead='2000000000000'/></RunTimeInformation>", "1,000,000,000,000", "2,000,000,000,000")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='9007199254740993' ActualRowsRead='18446744073709551615'/></RunTimeInformation>", "9,007,199,254,740,993", "18,446,744,073,709,551,615")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='18446744073709551615'/><RunTimeCountersPerThread ActualRows='1'/></RunTimeInformation>", "18,446,744,073,709,551,616", "N/A")]
    [InlineData("<RunTimeInformation><RunTimeCountersPerThread ActualRows='18446744073709551616' ActualRowsRead='1e3'/></RunTimeInformation>", "N/A", "N/A")]
    public void Rows_OnlyDisplayCompleteValidMetrics(string contents, string output, string read)
    {
        XElement relOp = RelOp(contents);
        var node = Build(relOp);
        node.ActualRows.Should().Be(output);
        node.ActualRowsRead.Should().Be(read);
        var properties = new PlanPropertyService().BuildProperties(relOp);
        properties.Should().Contain(new PlanPropertyItem("Runtime", "实际输出行（行）", output));
        properties.Should().Contain(new PlanPropertyItem("Runtime", "实际读取行（行）", read));
    }

    [Theory]
    [InlineData("IndexScan")]
    [InlineData("TableScan")]
    [InlineData("XcsScan")]
    public void Ordered_IsReadFromSupportedScanTypes(string type)
    {
        Build(RelOp($"<{type} Ordered='1'/>")).Ordered.Should().Be("True");
    }

    [Fact]
    public void ExecutionFacts_IgnoreForeignNamespaceAndPartialRuntimeModes()
    {
        var node = Build(RelOp("""
            <RunTimeInformation><RunTimeCountersPerThread ActualExecutionMode="Batch"/>
              <RunTimeCountersPerThread/></RunTimeInformation>
            <IndexScan xmlns="urn:foreign" Ordered="true"/>
            """));
        node.ExecutionMode.Should().Be("N/A");
        node.Ordered.Should().Be("N/A");
    }

    [Theory]
    [InlineData("priority='7'", "7")]
    [InlineData("priority='0'", "0")]
    [InlineData("priority='-5'", "-5")]
    [InlineData("priority='+07'", "7")]
    [InlineData("priority='7' currentdeadlockpriority='3' deadlockpriority='2' taskpriority='9'", "7")]
    [InlineData("priority='0' currentdeadlockpriority='7'", "0")]
    [InlineData("currentdeadlockpriority='3' deadlockpriority='2'", "3")]
    [InlineData("deadlockpriority='2'", "2")]
    [InlineData("", "")]
    [InlineData("taskpriority='7'", "")]
    [InlineData("priority='bad' currentdeadlockpriority='7'", "")]
    [InlineData("priority='' deadlockpriority='7'", "")]
    [InlineData("priority='2147483648'", "")]
    public void DeadlockPriority_UsesStandardFieldThenCompatibilityAliases(string attributes, string expected)
    {
        var document = SafeXmlHelper.ParseSafe($"""
            <deadlock><victim-list><victimProcess id="p1"/></victim-list>
            <process-list><process id="p1" spid="51" {attributes}/></process-list>
            <resource-list><keylock id="r1"><owner-list><owner id="p1"/></owner-list>
            <waiter-list><waiter id="p1"/></waiter-list></keylock></resource-list></deadlock>
            """);
        var result = DeadlockXmlParser.TryParseDeadlockXml(document);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Processes.Single().DeadlockPriority.Should().Be(expected);
        var process = result.Value.Processes.Single();
        new DeadlockSelectionDetailService().BuildProcessDetail(process).Should()
            .Contain($"死锁优先级: {(expected.Length == 0 ? "N/A" : expected)}");
        if (expected.Length == 0 && document.Descendants("process").Single().Attribute("priority") != null)
            result.Warnings.Should().Contain(w => w.Contains("priority"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("  true  ", true)]
    [InlineData("TRUE", null)]
    [InlineData("yes", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void PlanBooleans_KeepUnknownSeparateFromFalse(string? value, bool? expected)
    {
        XElement relOp = RelOp("<IndexScan/>");
        relOp.SetAttributeValue("Parallel", value);
        relOp.Element(XName.Get("IndexScan", Ns))!.SetAttributeValue("Ordered", value);
        var node = Build(relOp);
        ((object?)node.IsParallel).Should().Be(expected);
        node.Ordered.Should().Be(expected?.ToString() ?? "N/A");
    }

    [Theory]
    [InlineData("Batch", "Row", "Batch")]
    [InlineData("Row", "Batch", "Row")]
    [InlineData(null, "Batch", "Batch (估算)")]
    [InlineData(null, "Row", "Row (估算)")]
    [InlineData(null, null, "N/A")]
    [InlineData("invalid", null, "N/A")]
    public void ExecutionMode_UsesCurrentRuntimeThenExplicitlyLabelledEstimate(
        string? actual, string? estimated, string expected)
    {
        XElement relOp = RelOp("<RunTimeInformation><RunTimeCountersPerThread ActualRows='100'/></RunTimeInformation>");
        relOp.SetAttributeValue("EstimatedExecutionMode", estimated);
        relOp.Descendants(XName.Get("RunTimeCountersPerThread", Ns)).Single().SetAttributeValue("ActualExecutionMode", actual);
        Build(relOp).ExecutionMode.Should().Be(expected);
    }

    [Fact]
    public void ParentFacts_DoNotLeakFromChildrenOrOperatorNames()
    {
        XElement relOp = RelOp("""
            <Sort><RelOp Parallel="true" EstimatedExecutionMode="Batch">
              <RunTimeInformation><RunTimeCountersPerThread ActualExecutionMode="Batch" ActualRows="100"/></RunTimeInformation>
              <IndexScan Ordered="true"><ThreadStat/></IndexScan>
            </RelOp></Sort><InternalInfo Ordered="true"/>
            """);
        relOp.SetAttributeValue("PhysicalOp", "Parallelism");
        relOp.SetAttributeValue("LogicalOp", "Sort");
        var node = Build(relOp);
        ((object?)node.IsParallel).Should().BeNull();
        node.ExecutionMode.Should().Be("N/A");
        node.Ordered.Should().Be("N/A");
    }

    [Fact]
    public void MixedExecutionModes_AreNotCollapsedToOneThread()
    {
        var node = Build(RelOp("""
            <RunTimeInformation>
              <RunTimeCountersPerThread Thread="1" ActualExecutionMode="Batch" ActualRows="40"/>
              <RunTimeCountersPerThread Thread="2" ActualExecutionMode="Row" ActualRows="60"/>
            </RunTimeInformation>
            """));
        node.ExecutionMode.Should().Be("Mixed (Batch, Row)");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("zh-CN")]
    public void OutputAndReadRows_StayDistinctAcrossCultures(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var node = new PlanGraphNodeUiActionService().CreateNodeFromRelOp(RelOp("""
                <RunTimeInformation><RunTimeCountersPerThread ActualExecutionMode="Batch"
                  ActualRows="100" ActualRowsRead="1000"/></RunTimeInformation><IndexScan Ordered="true"/>
                """), Ns, 10, 1000);
            node.ActualRows.Should().Be("100");
            node.ActualRowsRead.Should().Be("1,000");
            node.ExecutionMode.Should().Be("Batch");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    internal static XElement RelOp(string contents = "") => SafeXmlHelper.ParseSafe(
        $"<RelOp xmlns='{Ns}' PhysicalOp='Index Scan' LogicalOp='Index Scan' EstimateRows='100'>{contents}</RelOp>").Root!;

    private static PlanGraphNodeBuildResult Build(XElement relOp) =>
        new PlanGraphNodeBuilderService().Build(relOp, Ns, new(10, 1000));
}
