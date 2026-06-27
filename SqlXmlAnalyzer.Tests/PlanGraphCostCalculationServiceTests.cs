using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphCostCalculationServiceTests
    {
        private readonly PlanGraphCostCalculationService _service = new();

        [Fact]
        public void Calculate_WhenNodesAreEmpty_ReturnsEmptyResult()
        {
            IReadOnlyList<PlanGraphNodeCostResult> result =
                _service.Calculate(Array.Empty<PlanGraphNodeCostInput>());

            result.Should().BeEmpty();
        }

        [Fact]
        public void Calculate_SubtractsChildSubtreeCostsFromOwnCost()
        {
            IReadOnlyList<PlanGraphNodeCostResult> result = _service.Calculate(
                new[]
                {
                    CreateInput(
                        subtreeCost: 100,
                        childSubtreeCosts: new[] { 20.0, 30.0 })
                });

            result[0].OwnCost.Should().Be(50);
            result[0].DisplayCost.Should().Be(50);
            result[0].CostPercent.Should().Be(50);
        }

        [Fact]
        public void Calculate_WhenChildrenExceedSubtreeCost_ClampsOwnCostToZero()
        {
            IReadOnlyList<PlanGraphNodeCostResult> result = _service.Calculate(
                new[]
                {
                    CreateInput(
                        subtreeCost: 10,
                        childSubtreeCosts: new[] { 20.0 })
                });

            result[0].OwnCost.Should().Be(0);
            result[0].DisplayCost.Should().Be(0);
            result[0].CostPercent.Should().Be(0);
        }

        [Fact]
        public void Calculate_WhenActualRowsExist_RecalculatesActualCost()
        {
            IReadOnlyList<PlanGraphNodeCostResult> result = _service.Calculate(
                new[]
                {
                    CreateInput(
                        subtreeCost: 100,
                        estimatedRows: 100,
                        actualRows: 500,
                        hasActualRows: true)
                });

            result[0].OwnCost.Should().Be(100);
            result[0].ActualRecost.Should().Be(500);
        }

        [Theory]
        [InlineData(0, 500, true)]
        [InlineData(100, 500, false)]
        public void Calculate_WhenActualRowsCannotBeUsed_KeepsOwnCostAsActualCost(
            double estimatedRows,
            double actualRows,
            bool hasActualRows)
        {
            IReadOnlyList<PlanGraphNodeCostResult> result = _service.Calculate(
                new[]
                {
                    CreateInput(
                        subtreeCost: 100,
                        estimatedRows: estimatedRows,
                        actualRows: actualRows,
                        hasActualRows: hasActualRows)
                });

            result[0].ActualRecost.Should().Be(100);
        }

        [Fact]
        public void Calculate_NormalizesCostCpuAndIoPercentages()
        {
            IReadOnlyList<PlanGraphNodeCostResult> result = _service.Calculate(
                new[]
                {
                    CreateInput(
                        subtreeCost: 100,
                        childSubtreeCosts: new[] { 40.0 },
                        estimatedCpuCost: 5,
                        estimatedIoCost: 2),
                    CreateInput(
                        subtreeCost: 50,
                        estimatedCpuCost: 2.5,
                        estimatedIoCost: 4)
                });

            result[0].CostPercent.Should().Be(60);
            result[0].CpuPercent.Should().Be(100);
            result[0].IoPercent.Should().Be(50);
            result[1].CostPercent.Should().Be(50);
            result[1].CpuPercent.Should().Be(50);
            result[1].IoPercent.Should().Be(100);
        }

        [Fact]
        public void Calculate_WhenMaxCostsAreZero_UsesFallbackDenominators()
        {
            IReadOnlyList<PlanGraphNodeCostResult> result = _service.Calculate(
                new[]
                {
                    CreateInput(
                        subtreeCost: 0,
                        estimatedCpuCost: 0,
                        estimatedIoCost: 0)
                });

            result[0].CostPercent.Should().Be(0);
            result[0].CpuPercent.Should().Be(0);
            result[0].IoPercent.Should().Be(0);
        }

        private static PlanGraphNodeCostInput CreateInput(
            double subtreeCost = 100,
            IReadOnlyList<double>? childSubtreeCosts = null,
            double estimatedCpuCost = 1,
            double estimatedIoCost = 1,
            double estimatedRows = 100,
            double actualRows = 100,
            bool hasActualRows = false)
        {
            return new PlanGraphNodeCostInput(
                subtreeCost,
                childSubtreeCosts ?? Array.Empty<double>(),
                estimatedCpuCost,
                estimatedIoCost,
                estimatedRows,
                actualRows,
                hasActualRows);
        }
    }
}
