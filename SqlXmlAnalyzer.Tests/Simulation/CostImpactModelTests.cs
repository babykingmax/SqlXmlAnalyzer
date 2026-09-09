using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.Simulation;

namespace SqlXmlAnalyzer.Tests.Simulation;

public sealed class CostImpactModelTests
{
    internal static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;

    [Fact]
    public void Simulation_UsesCompleteDenominatorAndOwnCostsBeforeEvaluatingCandidates()
    {
        var doc = Plan();
        var candidates = Candidates(doc);
        var first = CostImpactSimulator.Simulate(doc, candidates[0], Ns);
        first.TotalOwnCost.Value.Should().Be(110);
        first.RelatedOwnCost.Value.Should().Be(6);
        first.RelatedCostSharePercent.Should().Be(5.4545);
        first.Candidates.Single().RelatedOperators.Should().HaveCount(1); // Parent cannot borrow child Object.
        first.ReductionPercent.Should().BeNull();
        first.TotalOwnCost.Kind.Should().Be(PlanMetricKind.Estimated);
        first.OperatorCount.Should().Be(4);
    }

    [Fact]
    public void Simulation_ReversingStatementsAndCandidateOrderPreservesTotalsAndRanking()
    {
        CostImpactResult Read(bool reverse)
        {
            var doc = Plan(reverse);
            var candidates = Candidates(doc);
            return CostImpactSimulator.SimulateCandidates(doc, reverse ? candidates.Reverse() : candidates, Ns);
        }
        var first = Read(false);
        var second = Read(true);
        first.TotalOwnCost.Should().Be(second.TotalOwnCost);
        first.RelatedOwnCost.Should().Be(second.RelatedOwnCost);
        first.RelatedCostSharePercent.Should().Be(60).And.Be(second.RelatedCostSharePercent);
        first.Candidates.Select(c => c.RelatedOwnCost.Value).Order().Should()
            .Equal(second.Candidates.Select(c => c.RelatedOwnCost.Value).Order());
    }

    [Fact]
    public void Simulation_OverlappingCandidatesAndDuplicatesCountEachOperatorOnce()
    {
        var doc = Plan();
        var candidates = Candidates(doc);
        var duplicate = Candidates(doc)[0];
        duplicate.IncludeColumns.Add(new() { Name = "[V]", Usage = "INCLUDE" });
        var result = CostImpactSimulator.SimulateCandidates(doc, [candidates[0], duplicate, candidates[0]], Ns);
        result.RelatedOwnCost.Value.Should().Be(6);
        result.Candidates.Should().HaveCount(3);
        result.CombinationPolicy.Should().Contain("并集").And.Contain("不相加");
    }

    [Fact]
    public void Simulation_KeylessOrStaleLocationCannotBorrowAnotherStatement()
    {
        var doc = Plan();
        var candidate = Candidates(doc)[0];
        var other = Plan();
        var result = CostImpactSimulator.Simulate(other, candidate, Ns);
        result.RelatedOwnCost.Value.Should().BeNull();
        candidate.KeyColumns.Clear();
        CostImpactSimulator.Simulate(doc, candidate, Ns).RelatedOwnCost.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("bad")]
    public void Simulation_MissingOrInvalidCostDoesNotBecomeZero(string? value)
    {
        var doc = Plan();
        doc.Descendants(Ns + "RelOp").First().SetAttributeValue("EstimatedTotalSubtreeCost", value);
        var result = CostImpactSimulator.SimulateCandidates(doc, Candidates(doc), Ns);
        result.TotalOwnCost.IsAvailable.Should().BeFalse();
        result.TotalOwnCost.Value.Should().BeNull();
        result.RelatedCostSharePercent.Should().BeNull();
        result.ReductionPercent.Should().BeNull();
    }

    [Fact]
    public void Simulation_TrueZeroAndOverflowRemainDistinct()
    {
        var doc = Plan();
        foreach (var op in doc.Descendants(Ns + "RelOp")) op.SetAttributeValue("EstimatedTotalSubtreeCost", "0");
        var zero = CostImpactSimulator.SimulateCandidates(doc, Candidates(doc), Ns);
        zero.TotalOwnCost.Value.Should().Be(0);
        zero.RelatedOwnCost.Value.Should().Be(0);
        zero.RelatedCostSharePercent.Should().BeNull();
        foreach (var op in doc.Descendants(Ns + "RelOp")) op.SetAttributeValue("EstimatedTotalSubtreeCost", "1e308");
        var overflow = CostImpactSimulator.SimulateCandidates(doc, Candidates(doc), Ns);
        overflow.TotalOwnCost.State.Should().Be(PlanMetricState.Invalid);
        overflow.RelatedCostSharePercent.Should().BeNull();
    }

    [Fact]
    public void Simulation_NestedStatementsAreNotCountedThroughParentTraversal()
    {
        var doc = Plan();
        var statements = doc.Descendants(Ns + "StmtSimple").ToArray();
        statements[1].Remove();
        statements[0].Add(new XElement(Ns + "Statements", statements[1]));
        var result = CostImpactSimulator.SimulateCandidates(doc, Candidates(doc), Ns);
        result.TotalOwnCost.Value.Should().Be(110);
        result.OperatorCount.Should().Be(4);
    }

    [Fact]
    public void Simulation_JsonPublishesModelScopeCombinationAndUnknownForecast()
    {
        var doc = Plan();
        var result = CostImpactSimulator.SimulateCandidates(doc, Candidates(doc), Ns);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        json.RootElement.GetProperty("ReductionPercent").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("IsCalibrated").GetBoolean().Should().BeFalse();
        result.ModelVersion.Should().EndWith("/2.0.0");
        result.InputScope.Should().Contain("当前文档");
        result.Limitations.Should().Contain(l => l.Contains("不是可节省"));
    }

    [Fact]
    public void Simulation_ResultKeepsDefinitionSnapshotWhenCandidateIsEditedLater()
    {
        var doc = Plan();
        var candidate = Candidates(doc)[0];
        var result = CostImpactSimulator.Simulate(doc, candidate, Ns);
        candidate.KeyColumns[0].Name = "[Changed]";
        result.Candidates[0].Keys.Should().Equal("K");
    }

    [Fact]
    public void Facts_ChildCostPermutationDoesNotChangeOwnCostOrSimulation()
    {
        double Read(bool reverse)
        {
            string[] children = ["1e16", "1", "1"];
            var doc = SafeXmlHelper.ParseSafe($"""
                <ShowPlanXML xmlns="{Ns}"><BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
                <RelOp NodeId="0" EstimatedTotalSubtreeCost="1.0000000000000004e16"><Concat>
                {string.Join("", (reverse ? children.Reverse() : children).Select((cost, i) => $"<RelOp NodeId='{i + 1}' EstimatedTotalSubtreeCost='{cost}'/>"))}
                </Concat></RelOp></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>
                """);
            return PlanIdentityAdapter.GetDocument(doc)!.Operators[0].Facts!.OwnCost.Value!.Value;
        }
        Read(false).Should().Be(Read(true));
    }

    internal static MissingIndexSuggestion[] Candidates(XDocument doc) => PlanIdentityAdapter.GetDocument(doc)!.Operators
        .Where(op => op.Objects.Count == 1).Select(op => new MissingIndexSuggestion
        {
            ObjectIdentity = op.Objects[0].Identity, Location = op.Location with { Operator = null },
            KeyColumns = [new() { Name = "[K]", Usage = "EQUALITY" }]
        }).ToArray();

    internal static XDocument Plan(bool reverse = false)
    {
        string Statement(int total, int scan) => $"""
            <StmtSimple StatementSubTreeCost="{total}"><QueryPlan>
              <RelOp NodeId="0" PhysicalOp="Sort" EstimatedTotalSubtreeCost="{total}"><Sort Distinct="false">
                <RelOp NodeId="1" PhysicalOp="Table Scan" EstimatedTotalSubtreeCost="{scan}" TableCardinality="100" AvgRowSize="20" EstimateRows="2">
                  <OutputList><ColumnReference Database="[db]" Schema="[dbo]" Table="[T]" Column="K"/></OutputList>
                  <TableScan><Object Database="[db]" Schema="[dbo]" Table="[T]"/><Predicate><ScalarOperator ScalarString="[K]=1"/></Predicate></TableScan>
                </RelOp>
              </Sort></RelOp>
            </QueryPlan></StmtSimple>
            """;
        return SafeXmlHelper.ParseSafe($"""
            <ShowPlanXML xmlns="{Ns}"><BatchSequence><Batch><Statements>
            {(reverse ? Statement(100, 60) + Statement(10, 6) : Statement(10, 6) + Statement(100, 60))}
            </Statements></Batch></BatchSequence></ShowPlanXML>
            """);
    }
}
