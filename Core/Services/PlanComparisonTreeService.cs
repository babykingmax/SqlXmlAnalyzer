using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum PlanComparisonCostTrend
    {
        Neutral,
        Higher,
        Lower
    }

    public sealed record PlanComparisonTreeResult(
        PlanComparisonTreeNode? PlanA,
        PlanComparisonTreeNode? PlanB);

    public sealed record PlanComparisonTreeNode(
        XElement Source,
        string OperatorText,
        string CostText,
        PlanComparisonNodeState State,
        PlanComparisonCostTrend CostTrend,
        bool IsPlanB,
        IReadOnlyList<string> RuntimeDeltaTexts,
        IReadOnlyList<PlanComparisonTreeNode> Children);

    public sealed class PlanComparisonTreeService
    {
        public PlanComparisonTreeResult BuildTree(PlanComparisonResult comparison)
        {
            if (comparison == null)
            {
                throw new ArgumentNullException(nameof(comparison));
            }

            return new PlanComparisonTreeResult(
                comparison.PlanA == null ? null : BuildNode(comparison.PlanA, isPlanB: false),
                comparison.PlanB == null ? null : BuildNode(comparison.PlanB, isPlanB: true));
        }

        private static PlanComparisonTreeNode BuildNode(
            PlanComparisonNode node,
            bool isPlanB)
        {
            PlanComparisonCostTrend costTrend = GetCostTrend(node);

            return new PlanComparisonTreeNode(
                node.Source,
                GetOperatorText(node),
                GetCostText(node, costTrend),
                node.State,
                costTrend,
                isPlanB,
                node.RuntimeDeltas.Select(FormatRuntimeDelta).ToList(),
                node.Children.Select(child => BuildNode(child, isPlanB)).ToList());
        }

        private static string GetOperatorText(PlanComparisonNode node)
        {
            return node.State switch
            {
                PlanComparisonNodeState.Added => $"{node.PhysicalOp} [Added]",
                PlanComparisonNodeState.Removed => $"{node.PhysicalOp} [Removed]",
                PlanComparisonNodeState.OperatorChanged =>
                    $"{node.PhysicalOp} [from {node.OtherPhysicalOp}]",
                _ => node.PhysicalOp
            };
        }

        private static string GetCostText(
            PlanComparisonNode node,
            PlanComparisonCostTrend costTrend)
        {
            string text = FormattableString.Invariant($" (Cost: {node.Cost:F4})");

            if (costTrend == PlanComparisonCostTrend.Neutral)
            {
                return text;
            }

            string sign = node.CostPercentDelta > 0 ? "+" : "";
            return text + FormattableString.Invariant($" ({sign}{node.CostPercentDelta:F1}%)");
        }

        private static PlanComparisonCostTrend GetCostTrend(PlanComparisonNode node)
        {
            if (node.State != PlanComparisonNodeState.Unchanged ||
                Math.Abs(node.CostPercentDelta) <= 5)
            {
                return PlanComparisonCostTrend.Neutral;
            }

            return node.CostPercentDelta > 0
                ? PlanComparisonCostTrend.Higher
                : PlanComparisonCostTrend.Lower;
        }

        private static string FormatRuntimeDelta(RuntimeMetricDelta delta)
        {
            string value = delta.Value.ToString(CultureInfo.InvariantCulture);

            if (Math.Abs(delta.Delta) < 1e-9)
            {
                return $"{delta.Label}: {value}";
            }

            string sign = delta.Delta > 0 ? "+" : "-";
            string magnitude = Math.Abs(delta.Delta).ToString(CultureInfo.InvariantCulture);
            return $"{delta.Label}: {value} ({sign}{magnitude})";
        }
    }
}
