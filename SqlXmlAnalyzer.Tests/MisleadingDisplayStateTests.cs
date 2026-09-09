using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class MisleadingDisplayStateTests
{
    [Fact]
    public void RuntimeMetrics_TrackEachMetricIndependently()
    {
        PlanComparisonResult result = new PlanComparisonController().BuildComparison(
            MisleadingDisplayTests.Snapshot("<RunTimeCountersPerThread ActualElapsedms='100' ActualLogicalReads='200' ActualRowsRead='1000'/>"),
            MisleadingDisplayTests.Snapshot("<RunTimeCountersPerThread ActualLogicalReads='250'/>"), XNamespace.None);

        result.PlanB!.RuntimeDeltas.Single(d => d.Label == "Logical reads").Delta.Should().Be(50);
        result.PlanB.RuntimeDeltas.Single(d => d.Label == "Elapsed").Value.Should().BeNull();
        result.PlanB.RuntimeDeltas.Single(d => d.Label == "Rows read").Delta.Should().BeNull();
    }

    [Fact]
    public void RuntimeMetrics_AreScopedToCurrentOperatorAndNamespace()
    {
        XElement relOp = SafeXmlHelper.ParseSafe("""
            <RelOp xmlns="urn:plan">
              <RunTimeInformation xmlns="urn:extension"><RunTimeCountersPerThread ActualElapsedms="999"/></RunTimeInformation>
              <NestedLoops><RelOp><RunTimeInformation><RunTimeCountersPerThread ActualElapsedms="100" ActualLogicalReads="200"/></RunTimeInformation></RelOp></NestedLoops>
            </RelOp>
            """).Root!;
        var metrics = new PlanComparisonRuntimeMetricsService().Read(relOp);
        metrics.Elapsed.Should().BeNull();
        metrics.LogicalReads.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeMetrics_CompleteWorkersAreOrderIndependent(bool reverse)
    {
        XElement first = new("RunTimeCountersPerThread", new XAttribute("ActualElapsedms", 10), new XAttribute("ActualLogicalReads", 30));
        XElement second = new("RunTimeCountersPerThread", new XAttribute("ActualElapsedms", 20), new XAttribute("ActualLogicalReads", 40));
        var relOp = new XElement("RelOp", new XElement("RunTimeInformation", reverse ? new[] { second, first } : new[] { first, second }));
        var metrics = new PlanComparisonRuntimeMetricsService().Read(relOp);
        metrics.Elapsed.Should().Be(20);
        metrics.LogicalReads.Should().Be(70);
    }

    [Fact]
    public void DependencyNotice_IsStableThroughPlaybackResetFilterAndEmptyInput()
    {
        var vm = new DeadlockPlaybackViewModel([new DeadlockEvent { Description = "锁快照" }]);
        string notice = vm.InferenceNotice;
        vm.PlayCommand.Execute(null);
        vm.InferenceNotice.Should().Be(notice);
        vm.PlayCommand.Execute(null);
        vm.CurrentStep = 1;
        vm.FocusCriticalPath = true;
        vm.InferenceNotice.Should().Be(notice);
        vm.ResetCommand.Execute(null);
        vm.InferenceNotice.Should().Be(notice);
        new DeadlockPlaybackViewModel([]).InferenceNotice.Should().Be(notice);
        notice.Should().Contain("不代表锁事件真实发生顺序").And.Contain("间隔不是事件耗时");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void Sandbox_InvalidAssumptionsAreUnknown(double value)
    {
        var vm = new IndexSandboxViewModel(MisleadingDisplayTests.Suggestion()) { ReturnedRows = value };
        vm.TippingPointStatus.Should().Contain("N/A");
        vm.TippingPointStatusColor.Should().Be("#757575");
        vm.CostReductionSummary.Should().Contain("N/A").And.NotContain("%");
    }

    [Fact]
    public void Sandbox_CoverageAndColumnChangesDoNotRestorePrecisePromises()
    {
        var doc = SafeXmlHelper.ParseSafe("""
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
              <RelOp><IndexScan><Object Database="[TestDb]" Schema="[dbo]" Table="[Orders]"/></IndexScan><OutputList>
                <ColumnReference Database="[TestDb]" Schema="[dbo]" Table="[Orders]" Column="Id"/>
                <ColumnReference Database="[TestDb]" Schema="[dbo]" Table="[Orders]" Column="Price"/>
              </OutputList></RelOp>
              </QueryPlan></StmtSimple></Statements></Batch></BatchSequence>
            </ShowPlanXML>
            """);
        var suggestion = MisleadingDisplayTests.Suggestion();
        SqlXmlAnalyzer.Core.Services.IndexTargetResolver.BindSqlSuggestion(suggestion,
            doc.Descendants(doc.Root!.Name.Namespace + "StmtSimple").Single(), doc.Root.Name.Namespace).Should().BeTrue();
        var vm = new IndexSandboxViewModel(suggestion, doc);
        vm.IsCoveredIndex.Should().BeTrue();
        vm.TippingPointDetails.Should().Contain("覆盖并不保证 Seek");
        vm.RemoveIncludeColumnCommand.Execute(vm.IncludeColumns.Single());
        vm.IsCoveredIndex.Should().BeFalse();
        vm.SimulationNotice.Should().Contain("模型尚未校准");
        vm.CostReductionSummary.Should().NotContain("%");
        vm.TippingPointDetails.Should().NotContain("将始终").And.NotContain("会选择");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void CostSummary_InvalidCostDoesNotClaimImprovement(double cost)
    {
        var vm = new MainViewModel { PlanA = new PlanSnapshot { TotalCost = 10 }, PlanB = new PlanSnapshot { TotalCost = cost } };
        vm.CostDeltaText.Should().Contain("N/A");
        vm.CostDeltaColor.Should().Be("#757575");
    }
}
