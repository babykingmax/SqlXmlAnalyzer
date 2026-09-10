using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PlanOperatorTreeNode
    {
        public string Header { get; init; } = "";
        public XElement? Source { get; init; }
        public IReadOnlyList<PlanOperatorTreeNode> Children { get; init; } =
            Array.Empty<PlanOperatorTreeNode>();
    }

    public sealed class PlanVisualNode
    {
        public string PhysicalOp { get; init; } = "";
        public string LogicalOp { get; init; } = "";
        public double Cost { get; init; }
        public string EstRows { get; init; } = "0";
        public Brush CostColor { get; init; } = Brushes.Black;
        public ImageSource? OperatorIcon { get; init; }
        public IReadOnlyList<PlanVisualNode> Children { get; init; } =
            Array.Empty<PlanVisualNode>();
        public XElement? Tag { get; init; }
    }

    public sealed class PlanTreeService
    {
        public IReadOnlyList<PlanVisualNode> BuildScopedVisualTree(IReadOnlyList<XElement> operators, XNamespace ns) =>
            FindRoots(operators, ns).Select(root => BuildVisualNode(root, ns)).ToArray();

        public IReadOnlyList<PlanOperatorTreeNode> BuildScopedOperatorTree(IReadOnlyList<XElement> operators, XNamespace ns) =>
            FindRoots(operators, ns).Select(root => BuildOperatorNode(root, ns)).ToArray();

        private static IEnumerable<XElement> FindRoots(IReadOnlyList<XElement> operators, XNamespace ns)
        {
            var included = operators.ToHashSet();
            return operators.Where(op => !op.Ancestors(ns + "RelOp").Any(included.Contains));
        }
        public IReadOnlyList<PlanVisualNode> BuildVisualTree(
            XDocument doc,
            XNamespace ns)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (ns == null)
            {
                throw new ArgumentNullException(nameof(ns));
            }

            XElement? rootRelOp = FindRootRelOp(doc, ns);
            return rootRelOp == null
                ? Array.Empty<PlanVisualNode>()
                : new[] { BuildVisualNode(rootRelOp, ns) };
        }

        public PlanOperatorTreeNode? BuildOperatorTree(
            XDocument doc,
            XNamespace ns)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (ns == null)
            {
                throw new ArgumentNullException(nameof(ns));
            }

            XElement? rootRelOp = FindRootRelOp(doc, ns);
            return rootRelOp == null
                ? null
                : BuildOperatorNode(rootRelOp, ns);
        }

        private static XElement? FindRootRelOp(
            XDocument doc,
            XNamespace ns)
        {
            return doc.Descendants(ns + "RelOp").FirstOrDefault();
        }

        private static PlanVisualNode BuildVisualNode(
            XElement relOp,
            XNamespace ns)
        {
            string physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";
            string logicalOp = relOp.Attribute("LogicalOp")?.Value ?? "";
            var facts = PlanOperatorFactsService.Get(relOp, ns);
            string estRows = facts.EstimatedRows.Display();
            double cost = facts.SubtreeCost.Value ?? 0;

            return new PlanVisualNode
            {
                PhysicalOp = physicalOp,
                LogicalOp = logicalOp,
                Cost = cost,
                EstRows = estRows,
                CostColor = GetCostBrush(cost),
                OperatorIcon = PlanIconManager.GetIcon(physicalOp),
                Tag = relOp,
                Children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns)
                    .Select(child => BuildVisualNode(child, ns))
                    .ToList()
            };
        }

        private static PlanOperatorTreeNode BuildOperatorNode(
            XElement relOp,
            XNamespace ns)
        {
            string physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";
            string cost = PlanOperatorFactsService.Get(relOp, ns).SubtreeCost.Display();

            return new PlanOperatorTreeNode
            {
                Header = $"{physicalOp} (Cost: {cost})",
                Source = relOp,
                Children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns)
                    .Select(child => BuildOperatorNode(child, ns))
                    .ToList()
            };
        }

        private static Brush GetCostBrush(double cost)
        {
            if (cost > 10.0)
            {
                return Brushes.Red;
            }

            if (cost > 5.0)
            {
                return Brushes.DarkOrange;
            }

            return Brushes.Black;
        }
    }
}
