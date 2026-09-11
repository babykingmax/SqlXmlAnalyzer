using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanOperatorFactsServiceTests
{
    private static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;
    internal static XElement Parse(string body = "", string attributes = "") =>
        SafeXmlHelper.ParseSafe($"<RelOp xmlns='{Ns}' {attributes}>{body}</RelOp>").Root!;
    private static PlanOperatorFacts Read(XElement op) => PlanOperatorFactsService.Get(op, Ns);
    private static string Counter(string attributes) => $"<RunTimeCountersPerThread {attributes}/>";
    private static XElement Runtime(string counters, string attributes = "") => Parse($"<RunTimeInformation>{counters}</RunTimeInformation>", attributes);

    [Fact]
    public void Read_StopsAtChildOperatorsAndForeignOrInternalPayloads()
    {
        XElement op = Parse("""
            <NestedLoops><RelOp><IndexScan Partitioned='true'><Object Table='[Child]'/>
              <Predicate><ScalarOperator ScalarString='child=1'/></Predicate>
              <SeekPredicates><ScalarOperator ScalarString='seek=1'/></SeekPredicates>
            </IndexScan></RelOp></NestedLoops>
            <InternalInfo><Object Table='[Private]'/></InternalInfo>
            <foreign xmlns='foreign'><Object xmlns='http://schemas.microsoft.com/sqlserver/2004/07/showplan' Table='[Foreign]'/></foreign>
            """, "PhysicalOp='Nested Loops'");
        var facts = Read(op);
        facts.Objects.Should().BeEmpty();
        facts.Predicates.Should().BeEmpty();
        facts.SeekPredicates.Should().BeEmpty();
        facts.Partitioned.Should().BeNull();
        PlanDiagnosticAnalyzer.ExtractObjectName(op, Ns).Should().Be("(未知表)");
        new PlanGraphRelOpDetailsService().Parse(op, Ns, "Nested Loops").TableName.Should().BeEmpty();
        new PlanGraphNodeBuilderService().Build(op, Ns, new(10, 1000)).Predicate.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("IndexScan")]
    public void Read_ExtractsDirectAndPayloadPredicatesWithMultipleScalars(string wrapper)
    {
        string body = "<Predicate><ScalarOperator ScalarString='a=1'/><ScalarOperator ScalarString='b=2'/></Predicate>"
            + "<SeekPredicates><SeekPredicateNew><ScalarOperator ScalarString='c=3'/></SeekPredicateNew></SeekPredicates>";
        var op = Parse(wrapper == "" ? body : $"<{wrapper}>{body}</{wrapper}>");
        var facts = Read(op);
        facts.Predicates.Should().Equal("a=1", "b=2");
        facts.SeekPredicates.Should().Equal("c=3");
        PlanDiagnosticAnalyzer.ExtractResidualPredicate(op, Ns).Should().Be("a=1 AND b=2");
        PlanDiagnosticAnalyzer.ExtractSeekPredicate(op, Ns).Should().Be("c=3");
        new PlanPropertyService().BuildProperties(op).Should().Contain(new PlanPropertyItem("Predicate", "Residual Predicate", "a=1 AND b=2"));
    }

    [Fact]
    public void Read_ParallelWorkersMatchEstimateRegardlessOfThreadOrder()
    {
        string[] counters = Enumerable.Range(1, 16).Select(i => Counter($"Thread='{i}' ActualRows='1000' ActualRowsRead='2000' ActualExecutions='1'")).ToArray();
        foreach (string[] order in new[] { counters, counters.Reverse().ToArray() })
        {
            var op = Runtime(Counter("Thread='0' ActualRows='0' ActualRowsRead='0' ActualExecutions='1'") + string.Concat(order), "EstimateRows='16000' Parallel='true'");
            var facts = Read(op);
            facts.OutputRows.Value.Should().Be(16000);
            facts.RowsRead.Value.Should().Be(32000);
            facts.ThreadExecutions.Value.Should().Be(17);
            facts.LogicalExecutions.Value.Should().Be(1);
            facts.RowsPerExecution.Value.Should().Be(16000);
            facts.OutputRows.Aggregation.Should().Be(PlanMetricAggregation.SumThreads);
            new RowEstimateMismatchRule().Analyze(op, Ns).Should().BeNull();
            new CardinalityErrorRule().Analyze(op, Ns).Should().BeNull();
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 1000)]
    [InlineData(4, 1000)]
    public void Read_NormalizesRepeatedExecutionsWithoutSummingWorkerDenominators(int workers, int executions)
    {
        var op = Runtime(string.Concat(Enumerable.Range(1, workers).Select(i => Counter(
            $"Thread='{i}' ActualRows='{100 * executions}' ActualExecutions='{executions}'"))), $"EstimateRows='{100 * workers}'");
        var facts = Read(op);
        facts.RowsPerExecution.Value.Should().Be(100 * workers);
        facts.LogicalExecutions.Value.Should().Be(executions);
        new CardinalityErrorRule().Analyze(op, Ns).Should().BeNull();
        new RowEstimateMismatchRule().Analyze(op, Ns).Should().BeNull();
    }

    [Theory]
    [InlineData("Thread='1' ActualRows='1000' ActualExecutions='1'", "Thread='2' ActualRows='2000' ActualExecutions='2'")]
    [InlineData("Thread='0' ActualRows='1000' ActualExecutions='1'", "Thread='1' ActualRows='1000' ActualExecutions='1'")]
    [InlineData("ActualRows='1000' ActualExecutions='1'", "ActualRows='1000' ActualExecutions='1'")]
    public void Read_AmbiguousDenominatorPreservesTotalsButAbstainsFromComparison(string first, string second)
    {
        var op = Runtime(Counter(first) + Counter(second), "EstimateRows='100000'");
        Read(op).OutputRows.IsAvailable.Should().BeTrue();
        Read(op).RowsPerExecution.Value.Should().BeNull();
        new RowEstimateMismatchRule().Analyze(op, Ns).Should().BeNull();
        new CardinalityErrorRule().Analyze(op, Ns).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ActualRows='0'")]
    [InlineData("ActualRows='0' ActualExecutions='0'")]
    [InlineData("ActualRows='100' ActualExecutions='0'")]
    public void Read_MissingOrZeroExecutionsDoNotBecomeOne(string counter)
    {
        var facts = Read(Runtime(Counter(counter)));
        facts.RowsPerExecution.Value.Should().BeNull();
        if (!counter.Contains("ActualExecutions")) facts.ThreadExecutions.Value.Should().BeNull();
        else facts.ThreadExecutions.Value.Should().Be(0);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("18446744073709551616")]
    public void Read_InvalidCountersRemainUnavailableAndRetainRawValue(string value)
    {
        var facts = Read(Runtime(Counter($"ActualRows='{value}' ActualExecutions='1'")));
        facts.OutputRows.IsAvailable.Should().BeFalse();
        facts.OutputRows.Value.Should().BeNull();
        facts.Threads[0].OutputRows.State.Should().Be(PlanMetricState.Invalid);
        facts.Threads[0].SourceAttributes["ActualRows"].Should().Be(value);
    }

    [Fact]
    public void Read_PreservesUnsignedLongCountsExactlyAndSeparatesMissingFromZero()
    {
        var facts = Read(Runtime(Counter("Thread='1' ActualRows='18446744073709551615' ActualRowsRead='0'")
            + Counter("Thread='2' ActualRows='18446744073709551615' ActualRowsRead='0'")));
        facts.OutputRows.Value.Should().Be(36893488147419103230m);
        facts.RowsRead.Value.Should().Be(0);
        facts.ThreadExecutions.Value.Should().BeNull();
        facts.ThreadExecutions.IsPresent.Should().BeFalse();
        facts.RowsRead.IsPresent.Should().BeTrue();
    }

    [Fact]
    public void Read_PartialOrDuplicateCountersDoNotPublishPartialTotals()
    {
        var partial = Read(Runtime(Counter("Thread='1' ActualRows='50'") + Counter("Thread='2' ActualRowsRead='80'")));
        partial.OutputRows.State.Should().Be(PlanMetricState.Incomplete);
        partial.OutputRows.Value.Should().BeNull();
        partial.RowsRead.Value.Should().BeNull();
        var duplicate = Read(Runtime(Counter("Thread='1' ActualRows='50'") + Counter("Thread='1' ActualRows='80'")));
        duplicate.OutputRows.State.Should().Be(PlanMetricState.Ambiguous);
        duplicate.Threads.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("", PlanMetricState.Missing)]
    [InlineData("EstimateRows='NaN'", PlanMetricState.Invalid)]
    [InlineData("EstimateRows='-1'", PlanMetricState.Invalid)]
    [InlineData("EstimateRows='1e999'", PlanMetricState.Invalid)]
    public void Read_MissingAndInvalidEstimatesAreNotZero(string attributes, PlanMetricState state)
    {
        var facts = Read(Parse(attributes: attributes));
        facts.EstimatedRows.State.Should().Be(state);
        facts.EstimatedRows.Value.Should().BeNull();
        facts.EstimatedRowsRead.Value.Should().BeNull();
        facts.EstimatedExecutions.Value.Should().BeNull();
    }

    [Fact]
    public void Read_UsesInvariantNumbersAndDirectChildSubtreeCosts()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var op = Parse("<NestedLoops><RelOp EstimatedTotalSubtreeCost='4'><Sort><RelOp EstimatedTotalSubtreeCost='2'/></Sort></RelOp><RelOp EstimatedTotalSubtreeCost='3'/></NestedLoops>",
                "EstimateRows='1.5e3' EstimatedTotalSubtreeCost='10' EstimateRebinds='2' EstimateRewinds='3'");
            var facts = Read(op);
            facts.EstimatedRows.Value.Should().Be(1500);
            facts.SubtreeCost.Value.Should().Be(10);
            facts.OwnCost.Value.Should().Be(3);
            facts.OwnCost.Kind.Should().Be(PlanMetricKind.Estimated);
            facts.OwnCost.Unit.Should().Be("optimizer-cost");
            facts.EstimatedExecutions.Value.Should().Be(6);
            facts.OwnCost.Source.Should().Contain("direct child");
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Read_UnknownChildCostDoesNotBecomeZeroCost()
    {
        Read(Parse("<Sort><RelOp/></Sort>", "EstimatedTotalSubtreeCost='10'")).OwnCost.Value.Should().BeNull();
        Read(Parse("<Sort><RelOp EstimatedTotalSubtreeCost='20'/></Sort>", "EstimatedTotalSubtreeCost='10'")).OwnCost.State.Should().Be(PlanMetricState.Invalid);
    }

    [Fact]
    public void Read_UsesSumForCpuAndReadsButMaxForElapsedTime()
    {
        var op = Runtime(Counter("Thread='1' ActualElapsedms='8' ActualCPUms='5' ActualLogicalReads='10' ActualPhysicalReads='2'")
            + Counter("Thread='2' ActualElapsedms='10' ActualCPUms='7' ActualLogicalReads='20' ActualPhysicalReads='3'"));
        var facts = Read(op);
        facts.ElapsedTime.Value.Should().Be(10);
        facts.CpuTime.Value.Should().Be(12);
        facts.LogicalReads.Value.Should().Be(30);
        facts.PhysicalReads.Value.Should().Be(5);
        new PlanComparisonRuntimeMetricsService().Read(op).Should().Be(new PlanComparisonRuntimeMetrics(10, 30, null));
    }

    [Fact]
    public void Get_ReusesImmutableSnapshotAndInvalidatesAfterSourceMutation()
    {
        var op = Parse("<IndexScan><Object Table='[T]'/></IndexScan>", "EstimateRows='3'");
        var before = Read(op);
        Read(op).Should().BeSameAs(before);
        op.SetAttributeValue("EstimateRows", "4");
        Read(op).Should().NotBeSameAs(before);
        Read(op).EstimatedRows.Value.Should().Be(4);
        before.EstimatedRows.Value.Should().Be(3);
        Action modify = () => ((IDictionary<string, string>)before.Objects[0].SourceAttributes).Add("Table", "Changed");
        modify.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void PlanIdentityAndJsonExportUseSameFactsAsGraphAndRules()
    {
        string xml = EmbeddedResourceHelper.GetResourceContent("imp11_operator_facts.sqlplan");
        var input = new InputRecognitionService().Parse(xml);
        input.IsSuccess.Should().BeTrue();
        var plan = PlanIdentityAdapter.GetDocument(input.Document)!;
        var scan = plan.Operators.Single(o => o.Key.NodeId == "1");
        var source = plan.GetOperatorSource(scan.Key)!;
        var graph = new PlanGraphNodeBuilderService().Build(source, Ns, new(10, 1000));
        graph.Facts.Should().BeSameAs(scan.Facts);
        graph.ActualRows.Should().Be("100");
        graph.ActualRowsRead.Should().Be("1,000");
        graph.ExecutionMode.Should().Be("Batch");
        graph.Ordered.Should().Be("True");
        graph.Predicate.Should().Be("[T].[V]=(2)");
        new ResidualPredOpRule().Analyze(source, Ns).Should().NotBeNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(plan));
        var exported = json.RootElement.GetProperty("Batches")[0].GetProperty("Statements")[0].GetProperty("QueryPlans")[0].GetProperty("Operators")[1].GetProperty("Facts");
        exported.GetProperty("OutputRows").GetProperty("Value").GetDecimal().Should().Be(100);
        exported.GetProperty("RowsRead").GetProperty("Value").GetDecimal().Should().Be(1000);
        plan.Operators[0].Facts!.Objects.Should().BeEmpty();
    }

    [Fact]
    public void ResidualRule_WithoutMeasuredReadRowsDoesNotInventReadAmplification()
    {
        var op = Parse("<IndexScan><Predicate><ScalarOperator ScalarString='a=1'/></Predicate><SeekPredicates><ScalarOperator ScalarString='b=2'/></SeekPredicates></IndexScan>", "PhysicalOp='Index Seek'");
        new ResidualPredOpRule().Analyze(op, Ns).Should().BeNull();
    }

    [Fact]
    public void GraphAndConnection_RepeatedExecutionUsesComparableRowsWithoutLosingTotalFlow()
    {
        var op = Runtime(Counter("Thread='0' ActualRows='100000' ActualExecutions='1000'"), "EstimateRows='100' PhysicalOp='Index Seek'");
        var node = new Services.PlanGraphNodeUiActionService().CreateNodeFromRelOp(op, Ns, 10, 1000);
        node.SkewWarning.Should().BeEmpty();
        node.ActualRows.Should().Be("100,000");
        var connection = new ConnectionViewModel { Source = node };
        connection.RowsCount.Should().Be(100000);
        connection.ToolTipText.Should().Contain("估算偏差: 1.00 倍").And.Contain("逻辑执行次数: 1,000");
    }

    [Fact]
    public void Graph_UnknownOwnCostAndRowsRemainUnknownAfterCostRecalculation()
    {
        var op = Parse("<Sort><RelOp/></Sort>", "EstimatedTotalSubtreeCost='10'");
        var node = new Services.PlanGraphNodeUiActionService().CreateNodeFromRelOp(op, Ns, 10, 1000);
        new Services.PlanGraphCostUiActionService().ApplyCostCalculations([op],
            new Dictionary<XElement, PlanNodeViewModel> { [op] = node }, Ns, DiagramViewMode.CostPercent, PlanColorMode.TotalCost);
        node.PrimaryDisplayValue.Should().Be("估算成本 N/A");
        node.EstimatedOperatorCost.Should().Be("N/A");
        new ConnectionViewModel { Source = node }.LabelText.Should().Be("N/A");
        new PlanPropertyService().BuildProperties(op).Should().Contain(new PlanPropertyItem("Estimates", "Estimated Rows", "N/A"));
    }

    [Fact]
    public void LocalConversionDoesNotLeakToParentWarningOrRule()
    {
        var op = Parse("<NestedLoops><RelOp><ComputeScalar><ScalarOperator ScalarString='CONVERT_IMPLICIT(int,[T].[V],0)'/></ComputeScalar></RelOp></NestedLoops>", "NodeId='7' PhysicalOp='Nested Loops'");
        new ImplicitConversionRule().Analyze(op, Ns).Should().BeNull();
        new PlanGraphWarningService().BuildWarnings(op, Ns, new("7", "Nested Loops", "", "", false, false, 0, 0, false, 10, 1000),
            Array.Empty<AnalysisResult>()).WarningsText.Should().NotContain("CONVERT_IMPLICIT");
    }

    [Fact]
    public void Workers_ZeroExecutionIsExcludedFromDenominatorButZeroOutputParticipatesInSkew()
    {
        var facts = Read(Runtime(Counter("Thread='0' ActualRows='0' ActualExecutions='0'")
            + Counter("Thread='1' ActualRows='2000' ActualExecutions='2'")
            + Counter("Thread='2' ActualRows='0' ActualExecutions='0'")));
        facts.RowsPerExecution.Value.Should().Be(1000);
        facts.WorkerRows!.Count.Should().Be(2);
        facts.WorkerRows.Average.Should().Be(1000);
    }

    [Theory]
    [InlineData("ActualRows='0'")]
    [InlineData("ActualRows='0' ActualExecutions='0'")]
    [InlineData("ActualRows='bad' ActualExecutions='1'")]
    public void ZeroRowsRule_DoesNotDiagnoseWithoutExecutedAndCompleteEvidence(string attributes)
    {
        new ZeroRowActualsRule().Analyze(Runtime(Counter(attributes), "EstimateRows='1000'"), Ns).Should().BeNull();
    }

    [Fact]
    public void Get_CanceledReadDoesNotReturnCachedFacts()
    {
        var op = Parse();
        Read(op);
        Action read = () => PlanOperatorFactsService.Get(op, Ns, new CancellationToken(true));
        read.Should().Throw<OperationCanceledException>();
    }
}
