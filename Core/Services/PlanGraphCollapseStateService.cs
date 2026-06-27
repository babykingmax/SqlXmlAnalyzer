using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphCollapseStateNode(
        XElement RelOp,
        bool HasChildren,
        double SubtreeCost,
        string NodeSeverity,
        bool IsCollapsed);

    public sealed record PlanGraphCollapseStateResult(
        IReadOnlyDictionary<XElement, bool> CollapsedStates);

    public sealed class PlanGraphCollapseStateService
    {
        private readonly PlanGraphSmartCollapseService _smartCollapseService = new();

        public PlanGraphCollapseStateResult CalculateExpandAll(
            IReadOnlyList<PlanGraphCollapseStateNode> nodes)
        {
            ArgumentNullException.ThrowIfNull(nodes);

            return new PlanGraphCollapseStateResult(
                nodes.ToDictionary(
                    node => node.RelOp,
                    _ => false));
        }

        public PlanGraphCollapseStateResult CalculateSmartCollapse(
            IReadOnlyList<PlanGraphCollapseStateNode> nodes)
        {
            ArgumentNullException.ThrowIfNull(nodes);

            PlanGraphSmartCollapseResult result =
                _smartCollapseService.CalculateCollapsedRelOps(
                    nodes
                        .Select(node => new PlanGraphSmartCollapseNode(
                            node.RelOp,
                            node.HasChildren,
                            node.SubtreeCost,
                            node.NodeSeverity))
                        .ToList());

            return new PlanGraphCollapseStateResult(
                nodes.ToDictionary(
                    node => node.RelOp,
                    node => result.CollapsedRelOps.Contains(node.RelOp)));
        }

        public PlanGraphCollapseStateResult CalculateToggle(
            IReadOnlyList<PlanGraphCollapseStateNode> nodes,
            XElement targetRelOp)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(targetRelOp);

            return new PlanGraphCollapseStateResult(
                nodes.ToDictionary(
                    node => node.RelOp,
                    node => ReferenceEquals(node.RelOp, targetRelOp)
                        ? !node.IsCollapsed
                        : node.IsCollapsed));
        }
    }
}
