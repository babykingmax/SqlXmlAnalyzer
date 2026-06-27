using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphLayoutRefreshNode(
        XElement RelOp,
        bool IsCollapsed);

    public sealed record PlanGraphLayoutRefreshPosition(
        XElement RelOp,
        double X,
        double Y,
        double SubtreeWidth);

    public sealed record PlanGraphLayoutRefreshResult(
        IReadOnlyList<PlanGraphLayoutRefreshPosition> NodePositions,
        PlanGraphLayoutDirection ConnectionLayout);

    public sealed class PlanGraphLayoutRefreshService
    {
        private readonly PlanGraphLayoutService _layoutService = new();

        public PlanGraphLayoutRefreshResult Calculate(
            IReadOnlyList<XElement> allRelOps,
            XNamespace ns,
            IReadOnlyList<PlanGraphLayoutRefreshNode> nodes,
            PlanGraphLayoutDirection direction)
        {
            ArgumentNullException.ThrowIfNull(allRelOps);
            ArgumentNullException.ThrowIfNull(ns);
            ArgumentNullException.ThrowIfNull(nodes);

            if (allRelOps.Count == 0 || nodes.Count == 0)
            {
                return new PlanGraphLayoutRefreshResult(
                    Array.Empty<PlanGraphLayoutRefreshPosition>(),
                    direction);
            }

            var nodeRelOps = nodes
                .Select(node => node.RelOp)
                .ToHashSet();
            var collapsedRelOps = nodes
                .Where(node => node.IsCollapsed)
                .Select(node => node.RelOp)
                .ToHashSet();
            IReadOnlyList<PlanGraphLayoutPosition> positions =
                _layoutService.CalculateLayout(
                    allRelOps,
                    ns,
                    collapsedRelOps,
                    direction);

            return new PlanGraphLayoutRefreshResult(
                positions
                    .Where(position => nodeRelOps.Contains(position.Element))
                    .Select(position => new PlanGraphLayoutRefreshPosition(
                        position.Element,
                        position.X,
                        position.Y,
                        position.SubtreeWidth))
                    .ToList(),
                direction);
        }
    }
}
