using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphVisibilityStateNode(
        XElement RelOp,
        bool IsCollapsed);

    public sealed record PlanGraphVisibilityStateConnection(
        XElement SourceRelOp,
        XElement TargetRelOp);

    public sealed record PlanGraphVisibilityStateResult(
        IReadOnlySet<XElement> VisibleRelOps,
        IReadOnlySet<PlanGraphVisibilityStateConnection> VisibleConnections);

    public sealed class PlanGraphVisibilityStateService
    {
        private readonly PlanGraphVisibilityService _visibilityService = new();

        public PlanGraphVisibilityStateResult Calculate(
            IReadOnlyList<XElement> allRelOps,
            XNamespace ns,
            IReadOnlyList<PlanGraphVisibilityStateNode> nodes,
            IReadOnlyList<PlanGraphVisibilityStateConnection> connections)
        {
            ArgumentNullException.ThrowIfNull(allRelOps);
            ArgumentNullException.ThrowIfNull(ns);
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(connections);

            if (allRelOps.Count == 0 || nodes.Count == 0)
            {
                return new PlanGraphVisibilityStateResult(
                    new HashSet<XElement>(),
                    new HashSet<PlanGraphVisibilityStateConnection>());
            }

            var nodeRelOps = nodes
                .Select(node => node.RelOp)
                .ToHashSet();
            var collapsedRelOps = nodes
                .Where(node => node.IsCollapsed)
                .Select(node => node.RelOp)
                .ToHashSet();

            PlanGraphVisibilityResult visibility =
                _visibilityService.CalculateVisibility(
                    allRelOps,
                    ns,
                    collapsedRelOps);

            var visibleRelOps = visibility.VisibleRelOps
                .Where(nodeRelOps.Contains)
                .ToHashSet();
            var visibleConnectionPairs = visibility.VisibleConnections
                .Select(connection => new PlanGraphVisibilityStateConnection(
                    connection.SourceRelOp,
                    connection.TargetRelOp))
                .ToHashSet();
            var visibleConnections = connections
                .Where(visibleConnectionPairs.Contains)
                .ToHashSet();

            return new PlanGraphVisibilityStateResult(
                visibleRelOps,
                visibleConnections);
        }
    }
}
