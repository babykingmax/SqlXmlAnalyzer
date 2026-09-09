using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class MisleadingDisplayTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("18446744073709551616")]
    public void Comparison_MissingOrInvalidRuntime_IsUnknownWithoutNumericDelta(string? elapsed)
    {
        PlanSnapshot before = Snapshot("<RunTimeCountersPerThread ActualElapsedms='100' ActualLogicalReads='200'/>");
        string counters = elapsed == null ? "" : $"<RunTimeCountersPerThread ActualElapsedms='{elapsed}'/>";
        PlanComparisonResult comparison = new PlanComparisonController().BuildComparison(before, Snapshot(counters), Ns);
        PlanComparisonTreeNode display = new PlanComparisonTreeService().BuildTree(comparison).PlanB!;

        display.RuntimeDeltaTexts.Single(text => text.StartsWith("Elapsed")).Should().Contain("N/A");
        display.RuntimeDeltaTexts.Single(text => text.StartsWith("Logical reads")).Should().Contain("N/A");
        ((object?)comparison.PlanB!.RuntimeDeltas.Single(d => d.Label == "Elapsed").Delta).Should().BeNull();
    }

    [Fact]
    public void Comparison_CompleteZero_IsAnObservation()
    {
        PlanComparisonResult comparison = new PlanComparisonController().BuildComparison(
            Snapshot("<RunTimeCountersPerThread ActualElapsedms='100' ActualLogicalReads='200'/>"),
            Snapshot("<RunTimeCountersPerThread ActualElapsedms='0' ActualLogicalReads='0'/>"), Ns);

        comparison.PlanB!.RuntimeDeltas.Should().Contain(d => d.Label == "Elapsed" && d.Value == 0 && d.Delta == -100);
    }

    [Fact]
    public void Comparison_PartialThreadMetric_DoesNotUsePartialSumOrMaximum()
    {
        PlanComparisonResult comparison = new PlanComparisonController().BuildComparison(
            Snapshot("<RunTimeCountersPerThread ActualElapsedms='100' ActualLogicalReads='200'/>"),
            Snapshot("<RunTimeCountersPerThread ActualElapsedms='10' ActualLogicalReads='50'/><RunTimeCountersPerThread/>") , Ns);

        comparison.PlanB!.RuntimeDeltas.Should().AllSatisfy(d => ((object?)d.Delta).Should().BeNull());
    }

    [Fact]
    public void Comparison_ActualValueWithoutOtherObservation_PreservesValueButHasNoDelta()
    {
        PlanComparisonResult comparison = new PlanComparisonController().BuildComparison(
            Snapshot(""), Snapshot("<RunTimeCountersPerThread ActualElapsedms='0' ActualLogicalReads='200'/>"), Ns);

        comparison.PlanB!.RuntimeDeltas.Should().Contain(d => d.Label == "Logical reads" && d.Value == 200);
        comparison.PlanB.RuntimeDeltas.Should().AllSatisfy(d => ((object?)d.Delta).Should().BeNull());
    }

    [Theory]
    [InlineData("repeatable read (3)", "S")]
    [InlineData("serializable (4)", "X")]
    [InlineData("read committed (2)", "S")]
    [InlineData("", "STRANGE")]
    [InlineData("repeatable read", "RangeBogus")]
    public void RangeDiagnosis_RequiresRealRangeMode(string isolation, string mode)
    {
        DeadlockGraph graph = Graph(isolation, mode);

        DeadlockPatternAnalyzer.IdentifyPatterns(graph).Should().NotContain(p => p.TypeName.Contains("Range Lock"));
    }

    [Theory]
    [InlineData("RangeS-S")]
    [InlineData("RangeS-U")]
    [InlineData("RangeI-N")]
    [InlineData("RangeX-X")]
    public void RangeDiagnosis_ObservedModeDoesNotInventIsolationOrCause(string mode)
    {
        DeadlockPattern pattern = DeadlockPatternAnalyzer.IdentifyPatterns(Graph("read committed", mode))
            .Single(p => p.TypeName.Contains("Range Lock"));

        pattern.Description.Should().Contain("观测").And.Contain(mode);
        pattern.LikelyCause.Should().Contain("不能");
        pattern.TypeName.Should().NotContain("SERIALIZABLE");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SyntheticSteps_AreAlwaysCalledDependencyInference(int step)
    {
        var vm = new DeadlockPlaybackViewModel([
            new DeadlockEvent { Description = "持有锁" }, new DeadlockEvent { Description = "等待锁" }]);
        vm.CurrentStep = step;
        vm.CurrentStepDescription.Should().Contain("依赖推演").And.NotContain("回放死锁形成过程");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(700)]
    [InlineData(900)]
    public void Sandbox_HeuristicsDoNotPromiseOptimizerChoices(double returnedRows)
    {
        var vm = new IndexSandboxViewModel(Suggestion());
        vm.TotalRows = 100000;
        vm.AvgRowSize = 200;
        vm.ReturnedRows = returnedRows;

        vm.CostReductionDescription.Should().Contain("未校准");
        vm.TippingPointStatus.Should().Contain("假设");
        vm.TippingPointDetails.Should().Contain("不能").And.NotContain("将放弃").And.NotContain("会选择");
    }

    internal static PlanSnapshot Snapshot(string counters)
    {
        var snapshot = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1));
        var runtime = SafeXmlHelper.ParseSafe($"<RunTimeInformation xmlns='{Ns}'>{counters}</RunTimeInformation>").Root!;
        int thread = 0;
        foreach (var counter in runtime.Elements())
        {
            counter.SetAttributeValue("Thread", thread++);
            counter.SetAttributeValue("ActualExecutions", "1");
            counter.SetAttributeValue("ActualRows", "1");
            counter.SetAttributeValue("ActualExecutionMode", "Row");
        }
        snapshot.Document.Descendants(Ns + "RelOp").Single().Add(runtime);
        snapshot.TotalCost = 1;
        return snapshot;
    }

    internal static MissingIndexSuggestion Suggestion() => new()
    {
        Schema = "[dbo]", Table = "[Orders]", Impact = 90,
        KeyColumns = [new IndexColumn { Name = "[Id]", Usage = "EQUALITY" }],
        IncludeColumns = [new IndexColumn { Name = "[Price]", Usage = "INCLUDE" }]
    };

    private static DeadlockGraph Graph(string isolation, string mode)
    {
        var graph = new DeadlockGraph();
        graph.Processes.Add(new DeadlockProcess("p1", "50", "user", "host", isolation, "suspended", "", []));
        graph.Edges.Add(new WaitForEdge { FromProcessId = "p1", HeldMode = mode, RequestedMode = "X" });
        return graph;
    }
}
