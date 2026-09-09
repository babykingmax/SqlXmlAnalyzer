using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockGraphEdge(
        string FromId,
        string ToId,
        string Label,
        bool IsWaitEdge)
    {
        public string EvidenceId { get; init; } = "";
        public bool IsInCycle { get; init; }
        public double ParallelOffset { get; init; }
        public DeadlockPlaybackEdgeKey Key => new(FromId, ToId, EvidenceId);
    }

    public sealed class DeadlockGraphEdgeService
    {
        public IReadOnlyList<DeadlockGraphEdge> BuildEdges(
            IEnumerable<DeadlockGraphResourceNode> resources)
        {
            ArgumentNullException.ThrowIfNull(resources);

            var edges = new List<DeadlockGraphEdge>();

            foreach (DeadlockGraphResourceNode resource in resources)
            {
                if (resource.Links != null)
                {
                    foreach (var link in resource.Links)
                    {
                        string process = $"proc_id_{link.ProcessId}";
                        string mode = string.IsNullOrEmpty(link.Mode) ? link.RequestType : link.Mode;
                        edges.Add(new DeadlockGraphEdge(link.IsWaiter ? process : resource.Id,
                            link.IsWaiter ? resource.Id : process, FormatLabel(link.IsWaiter ? "Req" : "Own", mode), link.IsWaiter)
                        { EvidenceId = link.LinkId, IsInCycle = resource.CycleLinkIds.Contains(link.LinkId) });
                    }
                    continue;
                }
                LockResource? rawResource = resource.RawResources.FirstOrDefault();
                if (rawResource == null)
                {
                    continue;
                }

                foreach (string waiterId in resource.WaiterSpids)
                {
                    LockWaiter? waiter = rawResource.Waiters.FirstOrDefault(item => item.Id == waiterId);
                    string mode = ResolveWaitMode(rawResource, waiter);
                    edges.Add(new DeadlockGraphEdge(
                        $"proc_id_{waiterId}",
                        resource.Id,
                        FormatLabel("Req", mode),
                        IsWaitEdge: true));
                }

                foreach (string ownerId in resource.OwnerSpids)
                {
                    LockOwner? owner = rawResource.Owners.FirstOrDefault(item => item.Id == ownerId);
                    string mode = ResolveOwnerMode(rawResource, owner);
                    edges.Add(new DeadlockGraphEdge(
                        resource.Id,
                        $"proc_id_{ownerId}",
                        FormatLabel("Own", mode),
                        IsWaitEdge: false));
                }
            }

            foreach (var group in edges.Select((edge, index) => (edge, index)).GroupBy(item => (item.edge.FromId, item.edge.ToId)))
            {
                var parallel = group.OrderBy(item => item.edge.EvidenceId, StringComparer.Ordinal).ToArray();
                double spacing = 32.0 / Math.Max(1, parallel.Length - 1);
                for (int i = 0; i < parallel.Length; i++)
                    edges[parallel[i].index] = parallel[i].edge with { ParallelOffset = (i - (parallel.Length - 1) / 2.0) * spacing };
            }
            return edges;
        }

        private static string ResolveWaitMode(LockResource resource, LockWaiter? waiter)
        {
            string mode = waiter?.Mode ?? "";
            if (string.IsNullOrEmpty(mode))
            {
                mode = waiter?.RequestType ?? "";
            }

            return ResolveExchangeMode(resource, mode);
        }

        private static string ResolveOwnerMode(LockResource resource, LockOwner? owner)
        {
            return ResolveExchangeMode(resource, owner?.Mode ?? "");
        }

        private static string ResolveExchangeMode(LockResource resource, string mode)
        {
            if (string.IsNullOrEmpty(mode) && resource.LockType == "exchangeEvent")
            {
                return "Sync";
            }

            return mode;
        }

        private static string FormatLabel(string prefix, string mode)
        {
            return string.IsNullOrEmpty(mode)
                ? prefix
                : $"{prefix}: {mode}";
        }
    }
}
