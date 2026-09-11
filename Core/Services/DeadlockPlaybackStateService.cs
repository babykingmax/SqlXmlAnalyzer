using System;
using System.Collections.Generic;
using System.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockPlaybackEdgeKey(string FromId, string ToId, string EvidenceId = "");

    public sealed record DeadlockPlaybackNodeState(
        string NodeId,
        bool IsCollapsed,
        bool IsActive,
        bool IsVictim,
        bool IsVictimRevealed)
    {
        public bool IsInCycle { get; init; }
    }

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
            var observations = new Dictionary<DeadlockPlaybackEdgeKey, EdgeObservation>();
            var victimSteps = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (DeadlockEvent ev in timeline.Events)
            {
                if (ev.Type == "Victim")
                {
                    if (!victimSteps.TryGetValue(ev.ProcessId, out int previous) || ev.StepNumber < previous)
                        victimSteps[ev.ProcessId] = ev.StepNumber;
                }
                bool active = ev.StepNumber <= currentStep && (!focusCriticalPath || ev.IsInCycle);
                string processNodeId = ToProcessNodeId(ev.ProcessId);
                string resourceNodeId = ToResourceNodeId(ev.ResourceId);
                if (active)
                {
                    visibleNodes.Add(processNodeId);
                    if (!string.IsNullOrEmpty(ev.ResourceId)) visibleNodes.Add(resourceNodeId);
                }
                DeadlockPlaybackEdgeKey? key = ev.Type switch
                {
                    "Request" => new(processNodeId, resourceNodeId, ev.EvidenceId),
                    "Grant" => new(resourceNodeId, processNodeId, ev.EvidenceId),
                    _ => null
                };
                if (key != null)
                {
                    Observe(key, ev, active);
                    // Keep endpoint-only API callers working; identified edges never fall back
                    // to this aggregate and cannot accidentally reveal a sibling relationship.
                    if (!string.IsNullOrEmpty(key.EvidenceId)) Observe(key with { EvidenceId = "" }, ev, active);
                }
            }

            void Observe(DeadlockPlaybackEdgeKey key, DeadlockEvent ev, bool active)
            {
                observations.TryGetValue(key, out EdgeObservation previous);
                int? step = active ? ev.StepNumber : null;
                if (previous.FirstActiveStep.HasValue && (!step.HasValue || previous.FirstActiveStep < step)) step = previous.FirstActiveStep;
                observations[key] = new(previous.IsInCycle || ev.IsInCycle, step);
            }

            Dictionary<string, DeadlockPlaybackNodeState> nodeStates = nodeIds
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    nodeId => nodeId,
                    nodeId => CreateNodeState(
                        timeline,
                        nodeId,
                        visibleNodes,
                        victimSteps,
                        currentStep,
                        focusCriticalPath),
                    StringComparer.Ordinal);

            Dictionary<DeadlockPlaybackEdgeKey, DeadlockPlaybackEdgeState> edgeStates = edges
                .Distinct()
                .ToDictionary(
                    edge => edge,
                    edge => CreateEdgeState(
                        edge,
                        observations.GetValueOrDefault(edge),
                        focusCriticalPath));

            return new DeadlockPlaybackGraphState(nodeStates, edgeStates);
        }

        private static DeadlockPlaybackNodeState CreateNodeState(
            DeadlockTimelineParser.ParsedDeadlock timeline,
            string nodeId,
            IReadOnlySet<string> visibleNodes,
            IReadOnlyDictionary<string, int> victimSteps,
            int currentStep,
            bool focusCriticalPath)
        {
            string rawId = ToTimelineNodeId(nodeId);
            int? victimStep = victimSteps.TryGetValue(rawId, out int step) ? step : null;
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
                IsVictimRevealed: isVictim && victimStep.HasValue && currentStep >= victimStep.Value) { IsInCycle = inCycle };
        }

        private readonly record struct EdgeObservation(bool IsInCycle, int? FirstActiveStep);

        private static DeadlockPlaybackEdgeState CreateEdgeState(
            DeadlockPlaybackEdgeKey edge,
            EdgeObservation observation,
            bool focusCriticalPath)
        {
            return new DeadlockPlaybackEdgeState(
                edge,
                IsCollapsed: focusCriticalPath && !observation.IsInCycle,
                IsActive: observation.FirstActiveStep.HasValue,
                BadgeStepNumber: observation.FirstActiveStep);
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
