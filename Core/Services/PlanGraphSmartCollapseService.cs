using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphSmartCollapseNode(
        XElement RelOp,
        bool HasChildren,
        double SubtreeCost,
        string NodeSeverity);

    public sealed record PlanGraphSmartCollapseResult(
        IReadOnlySet<XElement> CollapsedRelOps);

    public sealed class PlanGraphSmartCollapseService
    {
        private const double DefaultCollapseThreshold = 0.05;

        public PlanGraphSmartCollapseResult CalculateCollapsedRelOps(
            IReadOnlyList<PlanGraphSmartCollapseNode> nodes,
            double collapseThreshold = DefaultCollapseThreshold)
        {
            ArgumentNullException.ThrowIfNull(nodes);

            if (nodes.Count == 0)
            {
                return new PlanGraphSmartCollapseResult(new HashSet<XElement>());
            }

            double maxSubtreeCost = nodes.Max(node => node.SubtreeCost);
            if (maxSubtreeCost <= 0)
            {
                maxSubtreeCost = 1.0;
            }

            HashSet<XElement> warningSubtree = BuildWarningSubtree(nodes);
            var collapsedRelOps = new HashSet<XElement>();

            foreach (PlanGraphSmartCollapseNode node in nodes)
            {
                if (node.HasChildren
                    && !warningSubtree.Contains(node.RelOp)
                    && node.SubtreeCost / maxSubtreeCost < collapseThreshold)
                {
                    collapsedRelOps.Add(node.RelOp);
                }
            }

            return new PlanGraphSmartCollapseResult(collapsedRelOps);
        }

        private static HashSet<XElement> BuildWarningSubtree(
            IReadOnlyList<PlanGraphSmartCollapseNode> nodes)
        {
            var warningSubtree = new HashSet<XElement>();

            foreach (PlanGraphSmartCollapseNode node in nodes)
            {
                if (node.NodeSeverity == "Info")
                {
                    continue;
                }

                XElement? ancestor = node.RelOp;
                while (ancestor != null && ancestor.Name.LocalName == "RelOp")
                {
                    warningSubtree.Add(ancestor);
                    ancestor = ancestor
                        .Parent
                        ?.AncestorsAndSelf()
                        .FirstOrDefault(a => a.Name.LocalName == "RelOp");
                }
            }

            return warningSubtree;
        }
    }
}
