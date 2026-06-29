using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphLayoutUiActionService
    {
        private readonly Core.Services.PlanGraphLayoutRefreshService _layoutRefreshService = new();

        public Core.Services.PlanGraphLayoutRefreshResult ApplyLayeredLayout(
            IReadOnlyList<XElement> relOps,
            XNamespace ns,
            IReadOnlyDictionary<XElement, PlanNodeViewModel> nodeMap,
            PlanLayoutMode layoutMode)
        {
            ArgumentNullException.ThrowIfNull(relOps);
            ArgumentNullException.ThrowIfNull(ns);
            ArgumentNullException.ThrowIfNull(nodeMap);

            Core.Services.PlanGraphLayoutRefreshResult result =
                _layoutRefreshService.Calculate(
                    relOps,
                    ns,
                    nodeMap
                        .Select(pair => new Core.Services.PlanGraphLayoutRefreshNode(
                            pair.Key,
                            pair.Value.IsCollapsed))
                        .ToList(),
                    ToGraphLayoutDirection(layoutMode));

            foreach (Core.Services.PlanGraphLayoutRefreshPosition position in result.NodePositions)
            {
                if (nodeMap.TryGetValue(position.RelOp, out PlanNodeViewModel? vm))
                {
                    vm.SubtreeWidth = position.SubtreeWidth;
                    vm.Location = new Point(position.X, position.Y);
                }
            }

            return result;
        }

        public void ReapplyLayout(
            XDocument? currentDocument,
            XNamespace? currentNamespace,
            IReadOnlyList<PlanNodeViewModel> masterNodes,
            IReadOnlyList<ConnectionViewModel> masterConnections,
            PlanLayoutMode layoutMode)
        {
            if (currentDocument == null
                || currentNamespace == null
                || masterNodes.Count == 0)
            {
                return;
            }

            XNamespace ns = currentNamespace;
            List<XElement> relOps =
                currentDocument.Descendants(ns + "RelOp").ToList();
            if (relOps.Count == 0)
            {
                return;
            }

            Dictionary<XElement, PlanNodeViewModel> nodeMap =
                masterNodes
                    .Where(node => node.RawElement != null)
                    .ToDictionary(node => node.RawElement!, node => node);

            Core.Services.PlanGraphLayoutRefreshResult layout =
                ApplyLayeredLayout(
                    relOps,
                    ns,
                    nodeMap,
                    layoutMode);

            PlanLayoutMode connectionLayout =
                ToPlanLayoutMode(layout.ConnectionLayout);
            foreach (ConnectionViewModel connection in masterConnections)
            {
                connection.LayoutMode = connectionLayout;
            }
        }

        private static Core.Services.PlanGraphLayoutDirection ToGraphLayoutDirection(
            PlanLayoutMode layoutMode)
        {
            return layoutMode == PlanLayoutMode.Horizontal
                ? Core.Services.PlanGraphLayoutDirection.Horizontal
                : Core.Services.PlanGraphLayoutDirection.Vertical;
        }

        private static PlanLayoutMode ToPlanLayoutMode(
            Core.Services.PlanGraphLayoutDirection layoutDirection)
        {
            return layoutDirection == Core.Services.PlanGraphLayoutDirection.Horizontal
                ? PlanLayoutMode.Horizontal
                : PlanLayoutMode.Vertical;
        }
    }
}
