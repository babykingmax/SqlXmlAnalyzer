using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphNodeCostInput(
        double SubtreeCost,
        IReadOnlyList<double> ChildSubtreeCosts,
        double EstimatedCpuCost,
        double EstimatedIoCost,
        double EstimatedRows,
        double ActualRows,
        bool HasActualRows);

    public sealed record PlanGraphNodeCostResult(
        double OwnCost,
        double DisplayCost,
        double ActualRecost,
        int CostPercent,
        double CpuPercent,
        double IoPercent);

    public sealed class PlanGraphCostCalculationService
    {
        public IReadOnlyList<PlanGraphNodeCostResult> Calculate(
            IReadOnlyList<PlanGraphNodeCostInput> nodes)
        {
            ArgumentNullException.ThrowIfNull(nodes);

            if (nodes.Count == 0)
            {
                return Array.Empty<PlanGraphNodeCostResult>();
            }

            double maxSubtreeCost = NormalizeMax(nodes.Max(node => node.SubtreeCost));
            double maxCpuCost = NormalizeMax(nodes.Max(node => node.EstimatedCpuCost));
            double maxIoCost = NormalizeMax(nodes.Max(node => node.EstimatedIoCost));

            return nodes
                .Select(node => CalculateNode(
                    node,
                    maxSubtreeCost,
                    maxCpuCost,
                    maxIoCost))
                .ToList();
        }

        private static PlanGraphNodeCostResult CalculateNode(
            PlanGraphNodeCostInput node,
            double maxSubtreeCost,
            double maxCpuCost,
            double maxIoCost)
        {
            double ownCost = Math.Max(
                0.0,
                node.SubtreeCost - node.ChildSubtreeCosts.Sum());
            double actualRecost = CalculateActualRecost(node, ownCost);

            return new PlanGraphNodeCostResult(
                ownCost,
                ownCost,
                actualRecost,
                CalculateCostPercent(ownCost, maxSubtreeCost),
                CalculatePercent(node.EstimatedCpuCost, maxCpuCost),
                CalculatePercent(node.EstimatedIoCost, maxIoCost));
        }

        private static double CalculateActualRecost(
            PlanGraphNodeCostInput node,
            double ownCost)
        {
            if (!node.HasActualRows || node.EstimatedRows <= 0)
            {
                return ownCost;
            }

            double actualRecost = ownCost * (node.ActualRows / node.EstimatedRows);
            return double.IsInfinity(actualRecost) || double.IsNaN(actualRecost)
                ? ownCost
                : actualRecost;
        }

        private static int CalculateCostPercent(
            double ownCost,
            double maxSubtreeCost)
        {
            double percent = (ownCost / maxSubtreeCost) * 100.0;
            return (int)Math.Min(100, Math.Max(0, Math.Round(percent)));
        }

        private static double CalculatePercent(
            double value,
            double maxValue)
        {
            double percent = (value / maxValue) * 100.0;
            return Math.Min(100.0, Math.Max(0.0, percent));
        }

        private static double NormalizeMax(double value)
        {
            return value <= 0 ? 1.0 : value;
        }
    }
}
