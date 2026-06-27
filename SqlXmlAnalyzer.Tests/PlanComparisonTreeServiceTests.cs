using System;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanComparisonTreeServiceTests
    {
        [Fact]
        public void BuildTree_FormatsOperatorChangeCostAndRuntimeDeltas()
        {
            var service = new PlanComparisonTreeService();
            XElement source = new("RelOp");
            var node = new PlanComparisonNode(
                source,
                "Nested Loops",
                "Hash Match",
                10,
                5,
                100,
                PlanComparisonNodeState.OperatorChanged,
                new[]
                {
                    new RuntimeMetricDelta("Elapsed", 20, 12),
                    new RuntimeMetricDelta("Rows read", 100, 0)
                },
                Array.Empty<PlanComparisonNode>());

            PlanComparisonTreeResult result = service.BuildTree(
                new PlanComparisonResult(node, null));

            result.PlanA.Should().NotBeNull();
            result.PlanA!.Source.Should().BeSameAs(source);
            result.PlanA.OperatorText.Should().Be("Nested Loops [from Hash Match]");
            result.PlanA.CostText.Should().Be(" (Cost: 10.0000)");
            result.PlanA.CostTrend.Should().Be(PlanComparisonCostTrend.Neutral);
            result.PlanA.RuntimeDeltaTexts.Should().Equal(
                "Elapsed: 20 (+12)",
                "Rows read: 100");
        }

        [Theory]
        [InlineData(PlanComparisonNodeState.Added, true, "Index Seek [Added]")]
        [InlineData(PlanComparisonNodeState.Removed, false, "Index Seek [Removed]")]
        public void BuildTree_FormatsAddedAndRemovedOperators(
            PlanComparisonNodeState state,
            bool isPlanB,
            string expectedText)
        {
            var service = new PlanComparisonTreeService();
            var node = CreateNode(
                physicalOp: "Index Seek",
                otherPhysicalOp: null,
                costPercentDelta: 0,
                state: state);

            PlanComparisonTreeResult result = service.BuildTree(new PlanComparisonResult(
                isPlanB ? null : node,
                isPlanB ? node : null));

            PlanComparisonTreeNode displayNode = isPlanB ? result.PlanB! : result.PlanA!;

            displayNode.OperatorText.Should().Be(expectedText);
            displayNode.IsPlanB.Should().Be(isPlanB);
        }

        [Theory]
        [InlineData(100, PlanComparisonCostTrend.Higher, " (Cost: 10.0000) (+100.0%)")]
        [InlineData(-20, PlanComparisonCostTrend.Lower, " (Cost: 10.0000) (-20.0%)")]
        [InlineData(4.9, PlanComparisonCostTrend.Neutral, " (Cost: 10.0000)")]
        public void BuildTree_FormatsCostTrendOnlyForMaterialUnchangedDeltas(
            double costPercentDelta,
            PlanComparisonCostTrend expectedTrend,
            string expectedText)
        {
            var service = new PlanComparisonTreeService();
            PlanComparisonNode node = CreateNode(
                physicalOp: "Sort",
                otherPhysicalOp: "Sort",
                costPercentDelta: costPercentDelta,
                state: PlanComparisonNodeState.Unchanged);

            PlanComparisonTreeNode displayNode = service.BuildTree(
                new PlanComparisonResult(node, null)).PlanA!;

            displayNode.CostTrend.Should().Be(expectedTrend);
            displayNode.CostText.Should().Be(expectedText);
        }

        [Fact]
        public void BuildTree_PreservesChildren()
        {
            var service = new PlanComparisonTreeService();
            var child = CreateNode(
                physicalOp: "Index Scan",
                otherPhysicalOp: "Index Seek",
                costPercentDelta: 0,
                state: PlanComparisonNodeState.OperatorChanged);
            var parent = CreateNode(
                physicalOp: "Hash Match",
                otherPhysicalOp: "Hash Match",
                costPercentDelta: 0,
                state: PlanComparisonNodeState.Unchanged,
                children: new[] { child });

            PlanComparisonTreeNode displayNode = service.BuildTree(
                new PlanComparisonResult(parent, null)).PlanA!;

            displayNode.Children.Should().ContainSingle();
            displayNode.Children.Single().OperatorText.Should().Be("Index Scan [from Index Seek]");
        }

        [Fact]
        public void BuildTree_WhenComparisonIsNull_Throws()
        {
            var service = new PlanComparisonTreeService();

            Action act = () => service.BuildTree(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        private static PlanComparisonNode CreateNode(
            string physicalOp,
            string? otherPhysicalOp,
            double costPercentDelta,
            PlanComparisonNodeState state,
            IReadOnlyList<PlanComparisonNode>? children = null)
        {
            return new PlanComparisonNode(
                new XElement("RelOp"),
                physicalOp,
                otherPhysicalOp,
                10,
                5,
                costPercentDelta,
                state,
                Array.Empty<RuntimeMetricDelta>(),
                children ?? Array.Empty<PlanComparisonNode>());
        }
    }
}
