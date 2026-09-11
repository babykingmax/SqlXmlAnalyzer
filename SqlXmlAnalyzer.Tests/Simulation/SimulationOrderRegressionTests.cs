using FluentAssertions;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Simulation;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests.Simulation;

public sealed class SimulationOrderRegressionTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    [Fact]
    public void Simulation_ReorderingStatementsDoesNotChangeResult()
    {
        XDocument Plan(bool reverse) => SafeXmlHelper.ParseSafe($"""
            <ShowPlanXML xmlns="{Ns}"><BatchSequence><Batch><Statements>
            {(reverse ? Statement(100) + Statement(10) : Statement(10) + Statement(100))}
            </Statements></Batch></BatchSequence></ShowPlanXML>
            """);
        var candidate = new MissingIndexSuggestion { Table = "[T]", KeyColumns = [new() { Name = "[K]", Usage = "EQUALITY" }] };
        var first = CostImpactSimulator.Simulate(Plan(false), candidate, Ns);
        var second = CostImpactSimulator.Simulate(Plan(true), candidate, Ns);
        first.ReductionPercent.Should().Be(second.ReductionPercent);
    }

    [Fact]
    public void Sandbox_ReorderingRepeatedAccessesDoesNotChangeAssumptions()
    {
        (double Rows, double Width, double Returned) Read(bool reverse)
        {
            string[] scans = [Scan(1, 100, 20, 2), Scan(2, 200, 40, 4)];
            var doc = SafeXmlHelper.ParseSafe($"""
                <ShowPlanXML xmlns="{Ns}"><BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
                <RelOp NodeId="0" PhysicalOp="Concatenation" EstimatedTotalSubtreeCost="2"><Concat>{string.Join("", reverse ? scans.Reverse() : scans)}</Concat></RelOp>
                </QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>
                """);
            var candidate = new MissingIndexSuggestion { Table = "[T]", KeyColumns = [new() { Name = "[K]", Usage = "EQUALITY" }] };
            Core.Services.IndexTargetResolver.BindSqlSuggestion(candidate, doc.Descendants(Ns + "StmtSimple").Single(), Ns).Should().BeTrue();
            var vm = new IndexSandboxViewModel(candidate, doc);
            return (vm.TotalRows, vm.AvgRowSize, vm.ReturnedRows);
        }
        Read(false).Should().Be(Read(true));
    }

    private static string Statement(int cost) => $"""<StmtSimple StatementSubTreeCost="{cost}"><QueryPlan><RelOp PhysicalOp="Table Scan" EstimatedTotalSubtreeCost="{cost}"><TableScan><Object Table="[T]"/></TableScan></RelOp></QueryPlan></StmtSimple>""";
    private static string Scan(int id, int rows, int width, int returned) => $"""<RelOp NodeId="{id}" PhysicalOp="Table Scan" EstimatedTotalSubtreeCost="1" TableCardinality="{rows}" AvgRowSize="{width}" EstimateRows="{returned}"><TableScan><Object Database="[db]" Schema="[dbo]" Table="[T]"/></TableScan></RelOp>""";
}
