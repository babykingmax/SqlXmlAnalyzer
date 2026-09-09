using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanOperatorFactsHardeningTests
{
    private static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;

    private static XElement Counter(string? thread, long rows = 20000, long executions = 1) =>
        new(Ns + "RunTimeCountersPerThread", thread == null ? null : new XAttribute("Thread", thread),
            new XAttribute("ActualRows", rows), new XAttribute("ActualRowsRead", rows * 2),
            new XAttribute("ActualExecutions", executions), new XAttribute("ActualEndOfScans", 1));

    private static XElement Operator(params XElement[] counters)
    {
        var op = PlanOperatorFactsServiceTests.Parse(attributes: "NodeId='1' PhysicalOp='Index Scan' EstimateRows='1' Parallel='true'");
        op.Add(new XElement(Ns + "RunTimeInformation", counters));
        return op;
    }

    [Theory]
    [InlineData("0")]
    [InlineData("+0")]
    [InlineData("-0")]
    [InlineData("0000")]
    [InlineData("+0000")]
    [InlineData(" \t+0000\r\n")]
    public void Read_EquivalentSerialThreadIdsPreserveCardinality(string thread)
    {
        var op = Operator(Counter(thread));
        var facts = PlanOperatorFactsService.Get(op, Ns);
        facts.Threads[0].ThreadId.Should().Be(XmlConvert.ToInt32(thread));
        facts.Threads[0].SourceAttributes["Thread"].Should().Be(thread);
        facts.OutputRows.Value.Should().Be(20000);
        facts.LogicalExecutions.Value.Should().Be(1);
        facts.RowsPerExecution.Value.Should().Be(20000);
        new CardinalityErrorRule().Analyze(op, Ns).Should().NotBeNull();
        new RowEstimateMismatchRule().Analyze(op, Ns).Should().NotBeNull();
    }

    [Fact]
    public void Read_SignedWorkersPreserveRepeatedExecutionAndSkewAcrossOrderings()
    {
        var counters = new[] { Counter("-0", 0), Counter("+01", 120000, 2), Counter(" 002 ", 0, 2), Counter("+3", 0, 2) };
        foreach (var order in new[] { counters, counters.Reverse().ToArray() })
        {
            var op = Operator(order.Select(c => new XElement(c)).ToArray());
            var facts = PlanOperatorFactsService.Get(op, Ns);
            facts.ThreadExecutions.Value.Should().Be(7);
            facts.LogicalExecutions.Value.Should().Be(2);
            facts.RowsPerExecution.Value.Should().Be(60000);
            facts.WorkerRows.Should().Be(new PlanWorkerRowDistribution(3, 120000, 120000, 40000));
            new ThreadSkewRule().Analyze(op, Ns).Should().NotBeNull();
            new ParallelSkewRule().Analyze(op, Ns).Should().NotBeNull();
            new PlanGraphRuntimeCountersService().Parse(op, Ns).IsThreadDataSkewed.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("0", "+0000")]
    [InlineData("-0", " 0 ")]
    [InlineData("01", "+1")]
    public void Read_EquivalentDuplicateIdsNeverDoubleCount(string first, string second)
    {
        var facts = PlanOperatorFactsService.Get(Operator(Counter(first), Counter(second)), Ns);
        facts.Threads.Should().HaveCount(2);
        facts.Threads.Select(t => t.SourceAttributes["Thread"]).Should().Equal(first, second);
        facts.OutputRows.State.Should().Be(PlanMetricState.Ambiguous);
        facts.OutputRows.Value.Should().BeNull();
        facts.ThreadExecutions.Value.Should().BeNull();
        facts.RowsPerExecution.Value.Should().BeNull();
        facts.WorkerRows.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("++1")]
    [InlineData("+ 1")]
    [InlineData("1+")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("1,000")]
    [InlineData("0x1")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("\u00a01\u00a0")]
    [InlineData("\u20031\u2003")]
    [InlineData("\u0661")]
    [InlineData("\uff0b1")]
    public void Read_InvalidThreadIdsPreserveRawCountsButDoNotInferRoles(string thread)
    {
        var op = Operator(Counter(thread), Counter("2", 0), Counter("3", 0));
        var facts = PlanOperatorFactsService.Get(op, Ns);
        facts.Threads[0].ThreadId.Should().BeNull();
        facts.Threads[0].SourceAttributes["Thread"].Should().Be(thread);
        facts.OutputRows.Value.Should().Be(20000);
        facts.ThreadExecutions.Value.Should().Be(3);
        facts.RowsPerExecution.State.Should().Be(PlanMetricState.Ambiguous);
        facts.WorkerRows.Should().BeNull();
        new CardinalityErrorRule().Analyze(op, Ns).Should().BeNull();
        new ThreadSkewRule().Analyze(op, Ns).Should().BeNull();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-2147483648")]
    public void Read_NegativeXsdIntIdsRemainRepresentableWithoutInventingWorkerRoles(string thread)
    {
        foreach (var op in new[] { Operator(Counter(thread)), Operator(Counter(thread), Counter("1", 0), Counter("2", 0)) })
        {
            var facts = PlanOperatorFactsService.Get(op, Ns);
            facts.Threads[0].ThreadId.Should().Be(XmlConvert.ToInt32(thread));
            facts.OutputRows.Value.Should().Be(20000);
            facts.RowsPerExecution.Value.Should().BeNull();
            facts.WorkerRows.Should().BeNull();
        }
    }

    [Fact]
    public void Read_Int32MaximumAndAsciiSignsAreIndependentOfCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.PositiveSign = "positive";
            culture.NumberFormat.NegativeSign = "negative";
            CultureInfo.CurrentCulture = culture;
            var facts = PlanOperatorFactsService.Get(Operator(Counter("+2147483647")), Ns);
            facts.Threads[0].ThreadId.Should().Be(int.MaxValue);
            facts.RowsPerExecution.Value.Should().Be(20000);
            PlanOperatorFactsService.Get(Operator(Counter("positive1")), Ns).Threads[0].ThreadId.Should().BeNull();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Get_ThreadMutationInvalidatesNormalizationAndDuplicateDetection()
    {
        var op = Operator(Counter("0"), Counter("+1"));
        var before = PlanOperatorFactsService.Get(op, Ns);
        op.Element(Ns + "RunTimeInformation")!.Elements().Last().SetAttributeValue("Thread", "+0000");
        var after = PlanOperatorFactsService.Get(op, Ns);
        before.OutputRows.Value.Should().Be(40000);
        after.Should().NotBeSameAs(before);
        after.OutputRows.State.Should().Be(PlanMetricState.Ambiguous);
        before.Threads[1].SourceAttributes["Thread"].Should().Be("+1");
    }

    [Fact]
    public void Read_SchemaValidSignedCoordinatorAgreesAcrossRulesGraphPropertiesAndJson()
    {
        var document = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("imp07_field_mapping.sqlplan"));
        var op = document.Descendants(Ns + "RelOp").Single();
        op.SetAttributeValue("EstimateRows", "1");
        op.SetAttributeValue("Parallel", "false");
        var runtime = op.Element(Ns + "RunTimeInformation")!;
        runtime.ReplaceNodes(Counter("+0"));
        var schemas = new XmlSchemaSet { XmlResolver = null };
        using var reader = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("showplanxml-sql2019.xsd")).CreateReader();
        schemas.Add(Ns.NamespaceName, reader);
        document.Validate(schemas, null);

        var plan = PlanIdentityAdapter.GetDocument(document)!;
        var facts = plan.Operators.Single().Facts!;
        var node = new Services.PlanGraphNodeUiActionService().CreateNodeFromRelOp(op, Ns, 10, 1000);
        node.Facts.Should().BeSameAs(facts);
        node.SkewWarning.Should().NotBeEmpty();
        var connection = new ConnectionViewModel { Source = node };
        connection.ToolTipText.Should().Contain("估算偏差: 20000.00 倍");
        new PlanPropertyService().BuildProperties(op).Should().Contain(new PlanPropertyItem("Runtime", "逻辑执行次数（次）", "1"));
        new CardinalityErrorRule().Analyze(op, Ns).Should().NotBeNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(plan));
        var exported = json.RootElement.GetProperty("Batches")[0].GetProperty("Statements")[0]
            .GetProperty("QueryPlans")[0].GetProperty("Operators")[0].GetProperty("Facts");
        exported.GetProperty("RowsPerExecution").GetProperty("Value").GetDecimal().Should().Be(20000);
        exported.GetProperty("Threads")[0].GetProperty("ThreadId").GetInt32().Should().Be(0);
        exported.GetProperty("Threads")[0].GetProperty("SourceAttributes").GetProperty("Thread").GetString().Should().Be("+0");
    }
}
