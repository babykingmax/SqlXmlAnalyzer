using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphVisibleConnection(
        XElement SourceRelOp,
        XElement TargetRelOp);

    public sealed record PlanGraphVisibilityResult(
        IReadOnlySet<XElement> VisibleRelOps,
        IReadOnlyList<PlanGraphVisibleConnection> VisibleConnections);

    public sealed class PlanGraphVisibilityService
    {
        public PlanGraphVisibilityResult CalculateVisibility(
            IReadOnlyList<XElement> relOps,
            XNamespace ns,
            ISet<XElement>? collapsedRelOps)
        {
            ArgumentNullException.ThrowIfNull(relOps);
            ArgumentNullException.ThrowIfNull(ns);

            if (relOps.Count == 0)
            {
                return new PlanGraphVisibilityResult(
                    new HashSet<XElement>(),
                    Array.Empty<PlanGraphVisibleConnection>());
            }

            ISet<XElement> collapsed = collapsedRelOps ?? new HashSet<XElement>();
            HashSet<XElement> relOpSet = relOps.ToHashSet();
            Dictionary<XElement, List<XElement>> childrenMap = relOps.ToDictionary(
                relOp => relOp,
                relOp => PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns).ToList());
            List<XElement> roots = FindRoots(relOps, childrenMap);

            var visibleRelOps = new HashSet<XElement>();
            var visibleConnections = new List<PlanGraphVisibleConnection>();

            foreach (XElement root in roots)
            {
                Traverse(
                    root,
                    isVisible: true,
                    collapsed,
                    relOpSet,
                    childrenMap,
                    visibleRelOps,
                    visibleConnections);
            }

            return new PlanGraphVisibilityResult(
                visibleRelOps,
                visibleConnections);
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

        private static void Traverse(
            XElement relOp,
            bool isVisible,
            ISet<XElement> collapsed,
            ISet<XElement> relOpSet,
            IReadOnlyDictionary<XElement, List<XElement>> childrenMap,
            ISet<XElement> visibleRelOps,
            IList<PlanGraphVisibleConnection> visibleConnections)
        {
            if (isVisible)
            {
                visibleRelOps.Add(relOp);
            }

            bool childrenVisible = isVisible && !collapsed.Contains(relOp);

            if (!childrenMap.TryGetValue(relOp, out List<XElement>? children))
            {
                return;
            }

            foreach (XElement child in children)
            {
                if (childrenVisible && relOpSet.Contains(child))
                {
                    visibleConnections.Add(
                        new PlanGraphVisibleConnection(child, relOp));
                }

                Traverse(
                    child,
                    childrenVisible,
                    collapsed,
                    relOpSet,
                    childrenMap,
                    visibleRelOps,
                    visibleConnections);
            }
        }
    }
}
