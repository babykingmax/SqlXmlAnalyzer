using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum PlanGraphLayoutDirection
    {
        Horizontal,
        Vertical
    }

    public sealed record PlanGraphLayoutPosition(
        XElement Element,
        double X,
        double Y,
        double SubtreeWidth);

    public sealed class PlanGraphLayoutService
    {
        private const double Origin = 50;
        private const double SiblingGap = 24;
        private const double HorizontalLevelGap = 72;
        private const double VerticalLevelGap = 64;

        public IReadOnlyList<PlanGraphLayoutPosition> CalculateLayout(
            IReadOnlyList<XElement> relOps,
            XNamespace ns,
            ISet<XElement>? collapsedRelOps,
            PlanGraphLayoutDirection direction)
        {
            ArgumentNullException.ThrowIfNull(relOps);
            ArgumentNullException.ThrowIfNull(ns);

            if (relOps.Count == 0)
            {
                return Array.Empty<PlanGraphLayoutPosition>();
            }

            var nodes = relOps.ToDictionary(element => element, element => new LayoutNode(element));
            foreach (LayoutNode node in nodes.Values)
            {
                // Only direct, included inputs are linked. A page boundary is a new
                // forest root, never a fabricated edge to an included grandparent.
                foreach (XElement childElement in PlanDiagnosticAnalyzer.GetDirectChildRelOps(node.Element!, ns))
                {
                    if (nodes.TryGetValue(childElement, out LayoutNode? child))
                    {
                        child.Parent = node;
                        child.Number = node.Children.Count;
                        node.Children.Add(child);
                    }
                }
            }

            var forest = new LayoutNode(null);
            foreach (XElement element in relOps)
            {
                LayoutNode node = nodes[element];
                if (node.Parent == null)
                {
                    node.Parent = forest;
                    node.Number = forest.Children.Count;
                    forest.Children.Add(node);
                }
            }

            // Explicit traversal and contour threads keep deep unary chains off
            // the CLR call stack. No subtree stores an array for every depth.
            var traversal = new List<LayoutNode>(relOps.Count + 1);
            var pending = new Stack<LayoutNode>();
            pending.Push(forest);
            while (pending.Count > 0)
            {
                LayoutNode node = pending.Pop();
                traversal.Add(node);
                if (node.Element != null && collapsedRelOps?.Contains(node.Element) == true)
                {
                    node.Children.Clear();
                }

                // Right-first preorder reversed below produces left-first postorder.
                foreach (LayoutNode child in node.Children)
                {
                    child.Depth = node.Depth + 1;
                    pending.Push(child);
                }
            }

            double crossStep = direction == PlanGraphLayoutDirection.Horizontal
                ? PlanGraphNodeMetrics.Height + SiblingGap
                : PlanGraphNodeMetrics.Width + SiblingGap;
            for (int index = traversal.Count - 1; index >= 0; index--)
            {
                FirstWalk(traversal[index], crossStep);
            }

            double minimumCross = double.PositiveInfinity;
            foreach (LayoutNode node in traversal)
            {
                node.Cross = node.Preliminary + (node.Parent?.AccumulatedModifier ?? 0);
                node.AccumulatedModifier = node.Modifier + (node.Parent?.AccumulatedModifier ?? 0);
                if (node.Element != null)
                {
                    minimumCross = Math.Min(minimumCross, node.Cross);
                }
            }

            double levelStep = direction == PlanGraphLayoutDirection.Horizontal
                ? PlanGraphNodeMetrics.Width + HorizontalLevelGap
                : PlanGraphNodeMetrics.Height + VerticalLevelGap;
            var positions = new Dictionary<XElement, PlanGraphLayoutPosition>();
            foreach (LayoutNode node in traversal)
            {
                if (node.Element == null) continue;
                double along = Origin + (node.Depth - 1) * levelStep;
                double cross = Origin + node.Cross - minimumCross;
                positions.Add(node.Element, new PlanGraphLayoutPosition(
                    node.Element,
                    direction == PlanGraphLayoutDirection.Horizontal ? along : cross,
                    direction == PlanGraphLayoutDirection.Horizontal ? cross : along,
                    node.LeafCount));
            }

            return relOps.Where(positions.ContainsKey).Select(element => positions[element]).ToList();
        }

        private static void FirstWalk(LayoutNode node, double separation)
        {
            LayoutNode? previous = node.PreviousSibling;
            if (node.Children.Count > 0)
            {
                ExecuteShifts(node);
                double midpoint = (node.Children[0].Preliminary + node.Children[^1].Preliminary) / 2;
                node.LeafCount = node.Children.Sum(child => child.LeafCount);
                if (previous != null)
                {
                    node.Preliminary = previous.Preliminary + separation;
                    node.Modifier = node.Preliminary - midpoint;
                }
                else
                {
                    node.Preliminary = midpoint;
                }
            }
            else if (previous != null)
            {
                node.Preliminary = previous.Preliminary + separation;
            }

            if (node.Parent != null)
            {
                node.Parent.DefaultAncestor = Apportion(
                    node,
                    node.Parent.DefaultAncestor ?? node.Parent.Children[0],
                    separation);
            }
        }

        private static LayoutNode Apportion(LayoutNode node, LayoutNode defaultAncestor, double separation)
        {
            LayoutNode? previous = node.PreviousSibling;
            if (previous == null) return defaultAncestor;

            LayoutNode insideRight = node;
            LayoutNode outsideRight = node;
            LayoutNode insideLeft = previous;
            LayoutNode outsideLeft = node.Parent!.Children[0];
            double insideRightModifier = insideRight.Modifier;
            double outsideRightModifier = outsideRight.Modifier;
            double insideLeftModifier = insideLeft.Modifier;
            double outsideLeftModifier = outsideLeft.Modifier;

            while (insideLeft.NextRight != null && insideRight.NextLeft != null)
            {
                insideLeft = insideLeft.NextRight;
                insideRight = insideRight.NextLeft;
                outsideLeft = outsideLeft.NextLeft!;
                outsideRight = outsideRight.NextRight!;
                outsideRight.Ancestor = node;
                double shift = insideLeft.Preliminary + insideLeftModifier
                    - insideRight.Preliminary - insideRightModifier + separation;
                if (shift > 0)
                {
                    LayoutNode ancestor = ReferenceEquals(insideLeft.Ancestor.Parent, node.Parent)
                        ? insideLeft.Ancestor
                        : defaultAncestor;
                    MoveSubtree(ancestor, node, shift);
                    insideRightModifier += shift;
                    outsideRightModifier += shift;
                }

                insideLeftModifier += insideLeft.Modifier;
                insideRightModifier += insideRight.Modifier;
                outsideLeftModifier += outsideLeft.Modifier;
                outsideRightModifier += outsideRight.Modifier;
            }

            if (insideLeft.NextRight != null && outsideRight.NextRight == null)
            {
                outsideRight.Thread = insideLeft.NextRight;
                outsideRight.Modifier += insideLeftModifier - outsideRightModifier;
            }
            if (insideRight.NextLeft != null && outsideLeft.NextLeft == null)
            {
                outsideLeft.Thread = insideRight.NextLeft;
                outsideLeft.Modifier += insideRightModifier - outsideLeftModifier;
                defaultAncestor = node;
            }

            return defaultAncestor;
        }

        private static void MoveSubtree(LayoutNode left, LayoutNode right, double shift)
        {
            double change = shift / (right.Number - left.Number);
            right.Change -= change;
            right.Shift += shift;
            left.Change += change;
            right.Preliminary += shift;
            right.Modifier += shift;
        }

        private static void ExecuteShifts(LayoutNode node)
        {
            double shift = 0;
            double change = 0;
            for (int index = node.Children.Count - 1; index >= 0; index--)
            {
                LayoutNode child = node.Children[index];
                child.Preliminary += shift;
                child.Modifier += shift;
                change += child.Change;
                shift += child.Shift + change;
            }
        }

        private sealed class LayoutNode
        {
            public LayoutNode(XElement? element)
            {
                Element = element;
                Ancestor = this;
            }

            public XElement? Element { get; }
            public List<LayoutNode> Children { get; } = new();
            public LayoutNode? Parent { get; set; }
            public LayoutNode Ancestor { get; set; }
            public LayoutNode? DefaultAncestor { get; set; }
            public LayoutNode? Thread { get; set; }
            public int Number { get; set; }
            public int Depth { get; set; }
            public double Preliminary { get; set; }
            public double Modifier { get; set; }
            public double Change { get; set; }
            public double Shift { get; set; }
            public double Cross { get; set; }
            public double AccumulatedModifier { get; set; }
            public double LeafCount { get; set; } = 1;
            public LayoutNode? PreviousSibling => Number > 0 ? Parent!.Children[Number - 1] : null;
            public LayoutNode? NextLeft => Children.Count > 0 ? Children[0] : Thread;
            public LayoutNode? NextRight => Children.Count > 0 ? Children[^1] : Thread;
        }
    }
}
