using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed record PlanGraphLoadUiActionResult(
        bool HasGraph,
        IReadOnlyList<PlanNodeViewModel> MasterNodes,
        IReadOnlyList<ConnectionViewModel> MasterConnections,
        PlanNodeViewModel? SelectedNode);

    internal sealed class PlanGraphLoadUiActionService
    {
        private readonly Core.Services.PlanGraphMissingIndexAssociationService _missingIndexAssociationService = new();
        private readonly PlanGraphConnectionUiActionService _connectionUiActionService = new();
        private readonly PlanGraphCostUiActionService _costUiActionService = new();
        private readonly PlanGraphLayoutUiActionService _layoutUiActionService = new();
        private readonly PlanGraphNodeUiActionService _nodeUiActionService = new();

        public PlanGraphLoadUiActionResult Load(
            XDocument? document,
            XNamespace ns,
            ObservableCollection<PlanNodeViewModel> nodes,
            ObservableCollection<ConnectionViewModel> connections,
            PlanGraphLoadUiActionOptions options)
        {
            ArgumentNullException.ThrowIfNull(ns);
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(connections);
            ArgumentNullException.ThrowIfNull(options);

            nodes.Clear();
            connections.Clear();

            if (document?.Root == null)
            {
                return EmptyResult();
            }

            List<XElement> relOps = document.Descendants(ns + "RelOp").ToList();
            if (relOps.Count == 0)
            {
                return EmptyResult();
            }

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            var allNodes = new List<PlanNodeViewModel>();

            foreach (XElement relOp in relOps)
            {
                PlanNodeViewModel vm =
                    _nodeUiActionService.CreateNodeFromRelOp(
                        relOp,
                        ns,
                        options.ResidualIoThreshold,
                        options.ResidualIoMinRowsRead);
                nodeMap[relOp] = vm;
                allNodes.Add(vm);
            }

            ApplyMissingIndexAssociations(document, ns, allNodes);

            _costUiActionService.ApplyCostCalculations(
                relOps,
                nodeMap,
                ns,
                options.InitialView,
                options.InitialColor);

            _layoutUiActionService.ApplyLayeredLayout(
                relOps,
                ns,
                nodeMap,
                options.InitialLayout);

            _connectionUiActionService.BuildConnections(
                relOps,
                ns,
                nodeMap,
                connections,
                options.InitialLayout,
                options.InitialLinkMetric);

            foreach (PlanNodeViewModel node in allNodes)
            {
                nodes.Add(node);
            }

            return new PlanGraphLoadUiActionResult(
                true,
                allNodes,
                connections.ToList(),
                allNodes.OrderByDescending(node => node.CostPercent).FirstOrDefault()
                    ?? allNodes.FirstOrDefault());
        }

        private void ApplyMissingIndexAssociations(
            XDocument document,
            XNamespace ns,
            IReadOnlyList<PlanNodeViewModel> allNodes)
        {
            IReadOnlyList<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion> missingIndexes =
                PlanDiagnosticAnalyzer.ExtractMissingIndexes(document, ns);
            IReadOnlyList<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion?> matchedSuggestions =
                _missingIndexAssociationService.MatchSuggestions(
                    allNodes
                        .Select(node => new Core.Services.PlanGraphMissingIndexNodeInfo(
                            node.TableName))
                        .ToList(),
                    missingIndexes);

            for (int i = 0; i < allNodes.Count; i++)
            {
                allNodes[i].AssociatedSuggestion = matchedSuggestions[i];
            }
        }

        private static PlanGraphLoadUiActionResult EmptyResult()
        {
            return new PlanGraphLoadUiActionResult(
                false,
                Array.Empty<PlanNodeViewModel>(),
                Array.Empty<ConnectionViewModel>(),
                null);
        }
    }

    internal sealed record PlanGraphLoadUiActionOptions
    {
        public required PlanLayoutMode InitialLayout { get; init; }
        public required PlanColorMode InitialColor { get; init; }
        public required DiagramViewMode InitialView { get; init; }
        public required LinkMetricMode InitialLinkMetric { get; init; }
        public required double ResidualIoThreshold { get; init; }
        public required int ResidualIoMinRowsRead { get; init; }
    }
}
