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
        private const double RootSpacing = 50;
        private const double HorizontalSpacing = 280;
        private const double VerticalSpacing = 160;

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

            ISet<XElement> collapsed = collapsedRelOps ?? new HashSet<XElement>();
            Dictionary<XElement, List<XElement>> childrenMap = BuildChildrenMap(relOps, ns);
            List<XElement> roots = FindRoots(relOps, childrenMap);
            Dictionary<XElement, double> subtreeWidths = new();
            Dictionary<XElement, PlanGraphLayoutPosition> positions = new();

            if (direction == PlanGraphLayoutDirection.Horizontal)
            {
                double currentY = Origin;
                foreach (XElement root in roots)
                {
                    double subtreeWidth = CalculateSubtreeWidth(root, childrenMap, collapsed, subtreeWidths);
                    SetHorizontalPositions(root, currentY, Origin, childrenMap, collapsed, subtreeWidths, positions);
                    currentY += subtreeWidth * VerticalSpacing + RootSpacing;
                }
            }
            else
            {
                double currentX = Origin;
                foreach (XElement root in roots)
                {
                    double subtreeWidth = CalculateSubtreeWidth(root, childrenMap, collapsed, subtreeWidths);
                    SetVerticalPositions(root, currentX, Origin, childrenMap, collapsed, subtreeWidths, positions);
                    currentX += subtreeWidth * HorizontalSpacing + RootSpacing;
                }
            }

            return relOps
                .Where(positions.ContainsKey)
                .Select(relOp => positions[relOp])
                .ToList();
        }

        private static Dictionary<XElement, List<XElement>> BuildChildrenMap(
            IReadOnlyList<XElement> relOps,
            XNamespace ns)
        {
            return relOps.ToDictionary(
                relOp => relOp,
                relOp => PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns).ToList());
        }

        private static List<XElement> FindRoots(
            IReadOnlyList<XElement> relOps,
            IReadOnlyDictionary<XElement, List<XElement>> childrenMap)
        {
            var childElements = new HashSet<XElement>(
                childrenMap.Values.SelectMany(children => children));
            var roots = relOps
                .Where(relOp => !childElements.Contains(relOp))
                .ToList();

            if (roots.Count == 0)
            {
                roots.Add(relOps[0]);
            }

            return roots;
        }

        private static double CalculateSubtreeWidth(
            XElement node,
            IReadOnlyDictionary<XElement, List<XElement>> childrenMap,
            ISet<XElement> collapsed,
            IDictionary<XElement, double> subtreeWidths)
        {
            if (subtreeWidths.TryGetValue(node, out double existingWidth))
            {
                return existingWidth;
            }

            if (collapsed.Contains(node)
                || !childrenMap.TryGetValue(node, out List<XElement>? children)
                || children.Count == 0)
            {
                subtreeWidths[node] = 1;
                return 1;
            }

            double totalWidth = children.Sum(child =>
                CalculateSubtreeWidth(child, childrenMap, collapsed, subtreeWidths));
            double subtreeWidth = Math.Max(1, totalWidth);
            subtreeWidths[node] = subtreeWidth;
            return subtreeWidth;
        }

        private static void SetHorizontalPositions(
            XElement node,
            double startY,
            double depthX,
            IReadOnlyDictionary<XElement, List<XElement>> childrenMap,
            ISet<XElement> collapsed,
            IReadOnlyDictionary<XElement, double> subtreeWidths,
            IDictionary<XElement, PlanGraphLayoutPosition> positions)
        {
            double subtreeWidth = subtreeWidths[node];
            positions[node] = new PlanGraphLayoutPosition(
                node,
                depthX,
                startY + (subtreeWidth - 1) * VerticalSpacing / 2,
                subtreeWidth);

            if (collapsed.Contains(node) || !childrenMap.TryGetValue(node, out List<XElement>? children))
            {
                return;
            }

            double childStartY = startY;
            foreach (XElement child in children)
            {
                SetHorizontalPositions(
                    child,
                    childStartY,
                    depthX + HorizontalSpacing,
                    childrenMap,
                    collapsed,
                    subtreeWidths,
                    positions);
                childStartY += subtreeWidths[child] * VerticalSpacing;
            }
        }

        private static void SetVerticalPositions(
            XElement node,
            double startX,
            double depthY,
            IReadOnlyDictionary<XElement, List<XElement>> childrenMap,
            ISet<XElement> collapsed,
            IReadOnlyDictionary<XElement, double> subtreeWidths,
            IDictionary<XElement, PlanGraphLayoutPosition> positions)
        {
            double subtreeWidth = subtreeWidths[node];
            positions[node] = new PlanGraphLayoutPosition(
                node,
                startX + (subtreeWidth - 1) * HorizontalSpacing / 2,
                depthY,
                subtreeWidth);

            if (collapsed.Contains(node) || !childrenMap.TryGetValue(node, out List<XElement>? children))
            {
                return;
            }

            double childStartX = startX;
            foreach (XElement child in children)
            {
                SetVerticalPositions(
                    child,
                    childStartX,
                    depthY + VerticalSpacing,
                    childrenMap,
                    collapsed,
                    subtreeWidths,
                    positions);
                childStartX += subtreeWidths[child] * HorizontalSpacing;
            }
        }
    }
}
