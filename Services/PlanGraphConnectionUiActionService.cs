using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphConnectionUiActionService
    {
        private readonly Core.Services.PlanGraphConnectionBuilderService _connectionBuilderService = new();
        private readonly Core.Services.PlanGraphConnectionHighlightService _highlightService = new();

        public void BuildConnections(
            IReadOnlyList<XElement> relOps,
            XNamespace ns,
            IReadOnlyDictionary<XElement, PlanNodeViewModel> nodeMap,
            ObservableCollection<ConnectionViewModel> connections,
            PlanLayoutMode initialLayout,
            LinkMetricMode initialLinkMetric)
        {
            ArgumentNullException.ThrowIfNull(relOps);
            ArgumentNullException.ThrowIfNull(ns);
            ArgumentNullException.ThrowIfNull(nodeMap);
            ArgumentNullException.ThrowIfNull(connections);

            foreach (Core.Services.PlanGraphConnectionPair connection in
                _connectionBuilderService.BuildConnections(relOps, ns))
            {
                if (nodeMap.TryGetValue(connection.SourceRelOp, out PlanNodeViewModel? sourceVm)
                    && nodeMap.TryGetValue(connection.TargetRelOp, out PlanNodeViewModel? targetVm))
                {
                    connections.Add(new ConnectionViewModel
                    {
                        Source = sourceVm,
                        Target = targetVm,
                        LayoutMode = initialLayout,
                        CurrentLinkMetric = initialLinkMetric
                    });
                }
            }
        }

        public void UpdateHighlights(
            string? selectedNodeId,
            IEnumerable<ConnectionViewModel> connections)
        {
            ArgumentNullException.ThrowIfNull(connections);

            foreach (ConnectionViewModel connection in connections)
            {
                connection.IsHighlighted = _highlightService.ShouldHighlight(
                    selectedNodeId,
                    connection.Source?.NodeId,
                    connection.Target?.NodeId);
            }
        }
    }
}
