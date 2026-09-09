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
        PlanComparisonTreeNode? PlanB)
    {
        public IReadOnlyList<PlanComparisonTreeNode> StatementsA { get; init; } = [];
        public IReadOnlyList<PlanComparisonTreeNode> StatementsB { get; init; } = [];
    }

    public sealed record PlanComparisonTreeNode(
        XElement Source,
        string OperatorText,
        string CostText,
        PlanComparisonNodeState State,
        PlanComparisonCostTrend CostTrend,
        bool IsPlanB,
        IReadOnlyList<string> RuntimeDeltaTexts,
        IReadOnlyList<PlanComparisonTreeNode> Children)
    {
        public string EvidenceText { get; init; } = "";
    }

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
                comparison.PlanB == null ? null : BuildNode(comparison.PlanB, isPlanB: true))
            {
                StatementsA = comparison.Statements.Select(s => Statement(s, false)).ToArray(),
                StatementsB = comparison.Statements.Select(s => Statement(s, true)).ToArray()
            };
        }

        private static PlanComparisonTreeNode Statement(StatementComparisonResult result, bool isB)
        {
            var statement = isB ? result.B : result.A;
            var query = isB ? result.QueryB : result.QueryA;
            var roots = isB ? result.RootsB : result.RootsA;
            return new(statement?.Source ?? (result.A ?? result.B)!.Source, result.Label, "",
                PlanComparisonNodeState.Unchanged, PlanComparisonCostTrend.Neutral, isB, [],
                roots.Select(root => BuildNode(root, isB)).ToArray())
            {
                EvidenceText = result.Detail + Environment.NewLine + (query?.Capture.Summary ?? "没有本侧计划")
                    + Environment.NewLine + (statement?.Statement.Text ?? "本侧未匹配")
            };
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
                node.Children.Select(child => BuildNode(child, isPlanB)).ToList())
            {
                EvidenceText = $"{node.Identity} ↔ {node.OtherIdentity}\n{node.Confidence}：{node.MatchEvidence}\n{node.CostReason}\n"
                    + string.Join("\n", node.RuntimeDeltas.Select(d => d.Source + "；" + d.Reason))
            };
        }

        private static string GetOperatorText(PlanComparisonNode node)
        {
            return node.State switch
            {
                PlanComparisonNodeState.Added => $"{node.PhysicalOp} [Added / B 未匹配]",
                PlanComparisonNodeState.Removed => $"{node.PhysicalOp} [Removed / A 未匹配]",
                PlanComparisonNodeState.OperatorChanged =>
                    $"{node.PhysicalOp} [from {node.OtherPhysicalOp}]",
                _ => node.PhysicalOp
            };
        }

        private static string GetCostText(
            PlanComparisonNode node,
            PlanComparisonCostTrend costTrend)
        {
            string text = node.Cost.HasValue ? FormattableString.Invariant($" (估算子树成本: {node.Cost:F4})") : " (估算子树成本: N/A)";
            if (!node.CostPercentDelta.HasValue) return text + (node.CostDelta is { } delta
                ? FormattableString.Invariant($" (差值 {delta:+0.####;-0.####;0}；百分比 N/A)") : " (差值/百分比 N/A)");

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
                !node.CostPercentDelta.HasValue || Math.Abs(node.CostPercentDelta.Value) <= 5)
            {
                return PlanComparisonCostTrend.Neutral;
            }

            return node.CostPercentDelta > 0
                ? PlanComparisonCostTrend.Higher
                : PlanComparisonCostTrend.Lower;
        }

        private static string FormatRuntimeDelta(RuntimeMetricDelta delta)
        {
            string label = delta.Label switch
            {
                "Elapsed" => "Elapsed (ms)",
                "Logical reads" => "Logical reads (页)",
                "Rows read" => "Rows read (行)",
                _ => delta.Label
            };
            string value = delta.Value?.ToString(CultureInfo.InvariantCulture) ?? "N/A";
            if (!delta.Value.HasValue || !delta.Delta.HasValue)
                return $"{label}: {value} (差值 N/A)";

            if (Math.Abs(delta.Delta.Value) < 1e-9)
            {
                return $"{label}: {value}";
            }

            string sign = delta.Delta > 0 ? "+" : "-";
            string magnitude = Math.Abs(delta.Delta.Value).ToString(CultureInfo.InvariantCulture);
            return $"{label}: {value} ({sign}{magnitude})";
        }
    }
}
