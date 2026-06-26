using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum PlanComparisonNodeState
    {
        Unchanged,
        Added,
        Removed,
        OperatorChanged
    }

    public sealed record RuntimeMetricDelta(
        string Label,
        double Value,
        double Delta);

    public sealed record PlanComparisonNode(
        XElement Source,
        string PhysicalOp,
        string? OtherPhysicalOp,
        double Cost,
        double OtherCost,
        double CostPercentDelta,
        PlanComparisonNodeState State,
        IReadOnlyList<RuntimeMetricDelta> RuntimeDeltas,
        IReadOnlyList<PlanComparisonNode> Children);

    public sealed record PlanComparisonResult(
        PlanComparisonNode? PlanA,
        PlanComparisonNode? PlanB);

    public sealed class PlanComparisonController
    {
        public PlanComparisonResult BuildComparison(
            PlanSnapshot? planA,
            PlanSnapshot? planB,
            XNamespace fallbackShowplanNamespace)
        {
            XElement? rootA = GetRootRelOp(planA, fallbackShowplanNamespace, out XNamespace nsA);
            XElement? rootB = GetRootRelOp(planB, fallbackShowplanNamespace, out XNamespace nsB);

            return new PlanComparisonResult(
                rootA == null ? null : BuildNode(rootA, rootB, nsA, isPlanB: false),
                rootB == null ? null : BuildNode(rootB, rootA, nsB, isPlanB: true));
        }

        private static XElement? GetRootRelOp(
            PlanSnapshot? snapshot,
            XNamespace fallbackShowplanNamespace,
            out XNamespace ns)
        {
            XElement? root = snapshot?.Document.Root;
            XNamespace defaultNamespace = root?.GetDefaultNamespace() ?? XNamespace.None;
            XNamespace elementNamespace = root?.Name.Namespace ?? XNamespace.None;
            ns = !string.IsNullOrEmpty(defaultNamespace.NamespaceName)
                ? defaultNamespace
                : !string.IsNullOrEmpty(elementNamespace.NamespaceName)
                    ? elementNamespace
                    : fallbackShowplanNamespace;
            return snapshot?.Document.Descendants(ns + "RelOp").FirstOrDefault();
        }

        private static PlanComparisonNode BuildNode(
            XElement currentRelOp,
            XElement? otherRelOp,
            XNamespace ns,
            bool isPlanB)
        {
            string physicalOp = currentRelOp.Attribute("PhysicalOp")?.Value ?? "Unknown";
            double cost = ParseDouble(currentRelOp.Attribute("EstimatedTotalSubtreeCost")?.Value);

            string? otherPhysicalOp = otherRelOp?.Attribute("PhysicalOp")?.Value;
            double otherCost = otherRelOp == null
                ? 0
                : ParseDouble(otherRelOp.Attribute("EstimatedTotalSubtreeCost")?.Value);

            PlanComparisonNodeState state = GetState(physicalOp, otherPhysicalOp, isPlanB);
            double costPercentDelta = otherRelOp != null && Math.Abs(otherCost) > 1e-9
                ? ((cost - otherCost) / otherCost) * 100
                : 0;

            IReadOnlyList<RuntimeMetricDelta> runtimeDeltas =
                BuildRuntimeDeltas(currentRelOp, otherRelOp, ns);

            var children = new List<PlanComparisonNode>();
            var currentChildren = PlanDiagnosticAnalyzer
                .GetDirectChildRelOps(currentRelOp, ns)
                .ToList();
            var otherChildren = otherRelOp != null
                ? PlanDiagnosticAnalyzer.GetDirectChildRelOps(otherRelOp, ns).ToList()
                : new List<XElement>();

            for (int i = 0; i < currentChildren.Count; i++)
            {
                XElement? otherChild = i < otherChildren.Count ? otherChildren[i] : null;
                children.Add(BuildNode(currentChildren[i], otherChild, ns, isPlanB));
            }

            return new PlanComparisonNode(
                currentRelOp,
                physicalOp,
                otherPhysicalOp,
                cost,
                otherCost,
                costPercentDelta,
                state,
                runtimeDeltas,
                children);
        }

        private static PlanComparisonNodeState GetState(
            string physicalOp,
            string? otherPhysicalOp,
            bool isPlanB)
        {
            if (otherPhysicalOp == null)
            {
                return isPlanB
                    ? PlanComparisonNodeState.Added
                    : PlanComparisonNodeState.Removed;
            }

            return physicalOp.Equals(otherPhysicalOp, StringComparison.Ordinal)
                ? PlanComparisonNodeState.Unchanged
                : PlanComparisonNodeState.OperatorChanged;
        }

        private static IReadOnlyList<RuntimeMetricDelta> BuildRuntimeDeltas(
            XElement currentRelOp,
            XElement? otherRelOp,
            XNamespace ns)
        {
            var currentMetrics = GetRuntimeMetrics(currentRelOp, ns);
            var otherMetrics = otherRelOp != null
                ? GetRuntimeMetrics(otherRelOp, ns)
                : (Rows: 0.0, RowsRead: 0.0, Elapsed: 0.0, Reads: 0.0);

            var deltas = new List<RuntimeMetricDelta>();
            AddDelta(deltas, "Elapsed", currentMetrics.Elapsed, currentMetrics.Elapsed - otherMetrics.Elapsed, 5);
            AddDelta(deltas, "Logical reads", currentMetrics.Reads, currentMetrics.Reads - otherMetrics.Reads, 10);
            AddDelta(deltas, "Rows read", currentMetrics.RowsRead, currentMetrics.RowsRead - otherMetrics.RowsRead, 10);
            return deltas;
        }

        private static void AddDelta(
            List<RuntimeMetricDelta> deltas,
            string label,
            double value,
            double delta,
            double threshold)
        {
            if (Math.Abs(delta) > threshold || (Math.Abs(delta) < 1e-9 && value > 0))
            {
                deltas.Add(new RuntimeMetricDelta(label, value, delta));
            }
        }

        private static (double Rows, double RowsRead, double Elapsed, double Reads) GetRuntimeMetrics(
            XElement relOp,
            XNamespace ns)
        {
            XElement? runtimeInfo = relOp.Element(ns + "RunTimeInformation");
            if (runtimeInfo == null)
            {
                return (0, 0, 0, 0);
            }

            List<XElement> counters = runtimeInfo
                .Elements(ns + "RunTimeCountersPerThread")
                .ToList();
            if (counters.Count == 0)
            {
                return (0, 0, 0, 0);
            }

            double rows = counters.Sum(counter => ParseDouble(counter.Attribute("ActualRows")?.Value));
            double rowsRead = counters.Sum(counter => ParseDouble(counter.Attribute("ActualRowsRead")?.Value));
            double elapsed = counters.Max(counter => ParseDouble(counter.Attribute("ActualElapsedms")?.Value));
            double reads = counters.Sum(counter => ParseDouble(counter.Attribute("ActualLogicalReads")?.Value));
            return (rows, rowsRead, elapsed, reads);
        }

        private static double ParseDouble(string? value)
        {
            return double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : 0;
        }
    }
}
