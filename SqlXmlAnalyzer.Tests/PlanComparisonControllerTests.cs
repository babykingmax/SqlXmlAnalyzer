using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanComparisonControllerTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void BuildComparison_WhenOperatorsDiffer_ReturnsOperatorChangedNodes()
        {
            var controller = new PlanComparisonController();
            var planA = CreateSnapshot("Nested Loops", 10, "Index Scan", 5, 20, 100, 1000);
            var planB = CreateSnapshot("Hash Match", 5, "Index Seek", 2, 8, 10, 100);

            PlanComparisonResult result = controller.BuildComparison(
                planA,
                planB,
                ShowplanNs);

            result.PlanA.Should().NotBeNull();
            result.PlanA!.State.Should().Be(PlanComparisonNodeState.OperatorChanged);
            result.PlanA.PhysicalOp.Should().Be("Nested Loops");
            result.PlanA.OtherPhysicalOp.Should().Be("Hash Match");
            result.PlanA.CostPercentDelta.Should().BeApproximately(100, 0.001);
            result.PlanA.RuntimeDeltas.Should().Contain(delta =>
                delta.Label == "Elapsed" &&
                delta.Value == 20 &&
                delta.Delta == 12);
            result.PlanA.Children.Should().ContainSingle();
            result.PlanA.Children[0].State.Should().Be(PlanComparisonNodeState.OperatorChanged);
        }

        [Fact]
        public void BuildComparison_WhenOtherPlanMissing_MarksPlanANodesAsRemoved()
        {
            var controller = new PlanComparisonController();
            var planA = CreateSnapshot("Index Scan", 4, null, 0, 0, 0, 0);

            PlanComparisonResult result = controller.BuildComparison(
                planA,
                null,
                ShowplanNs);

            result.PlanA.Should().NotBeNull();
            result.PlanA!.State.Should().Be(PlanComparisonNodeState.Removed);
            result.PlanB.Should().BeNull();
        }

        [Fact]
        public void BuildComparison_WhenPlanAIsMissing_MarksPlanBNodesAsAdded()
        {
            var controller = new PlanComparisonController();
            var planB = CreateSnapshot("Index Seek", 2, null, 0, 0, 0, 0);

            PlanComparisonResult result = controller.BuildComparison(
                null,
                planB,
                ShowplanNs);

            result.PlanA.Should().BeNull();
            result.PlanB.Should().NotBeNull();
            result.PlanB!.State.Should().Be(PlanComparisonNodeState.Added);
        }

        private static PlanSnapshot CreateSnapshot(
            string rootPhysicalOp,
            double rootCost,
            string? childPhysicalOp,
            double childCost,
            double elapsed,
            double reads,
            double rowsRead)
        {
            XElement rootRelOp = CreateRelOp(
                "0",
                rootPhysicalOp,
                rootCost,
                elapsed,
                reads,
                rowsRead);

            if (childPhysicalOp != null)
            {
                rootRelOp.Add(CreateRelOp("1", childPhysicalOp, childCost, 0, 0, 0));
            }

            var document = new XDocument(
                new XElement(
                    ShowplanNs + "ShowPlanXML",
                    new XElement(
                        ShowplanNs + "BatchSequence",
                        new XElement(
                            ShowplanNs + "Batch",
                            new XElement(
                                ShowplanNs + "Statements",
                                new XElement(
                                    ShowplanNs + "StmtSimple",
                                    new XElement(
                                        ShowplanNs + "QueryPlan",
                                        rootRelOp)))))));

            return new PlanSnapshot
            {
                Document = document,
                TotalCost = rootCost,
                OperatorCount = childPhysicalOp == null ? 1 : 2
            };
        }

        private static XElement CreateRelOp(
            string nodeId,
            string physicalOp,
            double cost,
            double elapsed,
            double reads,
            double rowsRead)
        {
            return new XElement(
                ShowplanNs + "RelOp",
                new XAttribute("NodeId", nodeId),
                new XAttribute("PhysicalOp", physicalOp),
                new XAttribute("EstimatedTotalSubtreeCost", cost),
                new XElement(
                    ShowplanNs + "RunTimeInformation",
                    new XElement(
                        ShowplanNs + "RunTimeCountersPerThread",
                        new XAttribute("ActualRows", 1),
                        new XAttribute("ActualRowsRead", rowsRead),
                        new XAttribute("ActualElapsedms", elapsed),
                        new XAttribute("ActualLogicalReads", reads))));
        }
    }
}
