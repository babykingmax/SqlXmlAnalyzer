using System;
using System.Collections.Generic;
using System.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockPlaybackEdgeKey(string FromId, string ToId);

    public sealed record DeadlockPlaybackNodeState(
        string NodeId,
        bool IsCollapsed,
        bool IsActive,
        bool IsVictim,
        bool IsVictimRevealed);

    public sealed record DeadlockPlaybackEdgeState(
        DeadlockPlaybackEdgeKey Edge,
        bool IsCollapsed,
        bool IsActive,
        int? BadgeStepNumber);

    public sealed record DeadlockPlaybackGraphState(
        IReadOnlyDictionary<string, DeadlockPlaybackNodeState> Nodes,
        IReadOnlyDictionary<DeadlockPlaybackEdgeKey, DeadlockPlaybackEdgeState> Edges);

    public sealed class DeadlockPlaybackStateService
    {
        public DeadlockPlaybackGraphState BuildState(
            DeadlockTimelineParser.ParsedDeadlock timeline,
            int currentStep,
            bool focusCriticalPath,
            IEnumerable<string> nodeIds,
            IEnumerable<DeadlockPlaybackEdgeKey> edges)
        {
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentNullException.ThrowIfNull(nodeIds);
            ArgumentNullException.ThrowIfNull(edges);

            HashSet<string> visibleNodes = new(StringComparer.Ordinal);
            HashSet<DeadlockPlaybackEdgeKey> visibleEdges = new();

            foreach (DeadlockEvent ev in timeline.Events)
            {
                if (ev.StepNumber > currentStep)
                {
                    continue;
                }

                if (focusCriticalPath && !ev.IsInCycle)
                {
                    continue;
                }

                string processNodeId = ToProcessNodeId(ev.ProcessId);
                string resourceNodeId = ToResourceNodeId(ev.ResourceId);

                visibleNodes.Add(processNodeId);
                visibleNodes.Add(resourceNodeId);

                if (ev.Type == "Request")
                {
                    visibleEdges.Add(new DeadlockPlaybackEdgeKey(processNodeId, resourceNodeId));
                }
                else if (ev.Type == "Grant")
                {
                    visibleEdges.Add(new DeadlockPlaybackEdgeKey(resourceNodeId, processNodeId));
                }
            }

            int? victimStep = timeline.Events.FirstOrDefault(ev => ev.Type == "Victim")?.StepNumber;

            Dictionary<string, DeadlockPlaybackNodeState> nodeStates = nodeIds
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    nodeId => nodeId,
                    nodeId => CreateNodeState(
                        timeline,
                        nodeId,
                        visibleNodes,
                        victimStep,
                        currentStep,
                        focusCriticalPath),
                    StringComparer.Ordinal);

            Dictionary<DeadlockPlaybackEdgeKey, DeadlockPlaybackEdgeState> edgeStates = edges
                .Distinct()
                .ToDictionary(
                    edge => edge,
                    edge => CreateEdgeState(
                        timeline,
                        edge,
                        visibleEdges,
                        focusCriticalPath));

            return new DeadlockPlaybackGraphState(nodeStates, edgeStates);
        }

        private static DeadlockPlaybackNodeState CreateNodeState(
            DeadlockTimelineParser.ParsedDeadlock timeline,
            string nodeId,
            IReadOnlySet<string> visibleNodes,
            int? victimStep,
            int currentStep,
            bool focusCriticalPath)
        {
            string rawId = ToTimelineNodeId(nodeId);
            bool isProcess = nodeId.StartsWith("proc_id_", StringComparison.Ordinal);
            bool inCycle = isProcess
                ? timeline.Processes.TryGetValue(rawId, out DeadlockNodeInfo? process) && process.IsInCycle
                : timeline.Resources.TryGetValue(rawId, out DeadlockResourceInfo? resource) && resource.IsInCycle;

            bool isVictim =
                isProcess &&
                timeline.Processes.TryGetValue(rawId, out DeadlockNodeInfo? victimProcess) &&
                victimProcess.IsVictim;

            return new DeadlockPlaybackNodeState(
                nodeId,
                IsCollapsed: focusCriticalPath && !inCycle,
                IsActive: visibleNodes.Contains(nodeId),
                IsVictim: isVictim,
                IsVictimRevealed: isVictim && victimStep.HasValue && currentStep >= victimStep.Value);
        }

        private static DeadlockPlaybackEdgeState CreateEdgeState(
            DeadlockTimelineParser.ParsedDeadlock timeline,
            DeadlockPlaybackEdgeKey edge,
            IReadOnlySet<DeadlockPlaybackEdgeKey> visibleEdges,
            bool focusCriticalPath)
        {
            DeadlockEvent? relatedEvent = FindRelatedEvent(timeline.Events, edge);
            bool inCycle = relatedEvent != null && relatedEvent.IsInCycle;
            bool isActive = visibleEdges.Contains(edge);

            return new DeadlockPlaybackEdgeState(
                edge,
                IsCollapsed: focusCriticalPath && !inCycle,
                IsActive: isActive,
                BadgeStepNumber: isActive ? relatedEvent?.StepNumber : null);
        }

        private static DeadlockEvent? FindRelatedEvent(
            IEnumerable<DeadlockEvent> events,
            DeadlockPlaybackEdgeKey edge)
        {
            return events.FirstOrDefault(ev =>
                ev.Type == "Request" &&
                ToProcessNodeId(ev.ProcessId) == edge.FromId &&
                ToResourceNodeId(ev.ResourceId) == edge.ToId)
                ?? events.FirstOrDefault(ev =>
                    ev.Type == "Grant" &&
                    ToResourceNodeId(ev.ResourceId) == edge.FromId &&
                    ToProcessNodeId(ev.ProcessId) == edge.ToId);
        }

        private static string ToProcessNodeId(string processId)
        {
            return $"proc_id_{processId}";
        }

        private static string ToResourceNodeId(string resourceId)
        {
            return resourceId.StartsWith("res_", StringComparison.Ordinal)
                ? resourceId.Replace("res_", "res_single_", StringComparison.Ordinal)
                : resourceId;
        }

        private static string ToTimelineNodeId(string nodeId)
        {
            if (nodeId.StartsWith("proc_id_", StringComparison.Ordinal))
            {
                return nodeId["proc_id_".Length..];
            }

            return nodeId.StartsWith("res_single_", StringComparison.Ordinal)
                ? nodeId.Replace("res_single_", "res_", StringComparison.Ordinal)
                : nodeId;
        }
    }
}
