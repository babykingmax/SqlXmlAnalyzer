using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockGraphEdge(
        string FromId,
        string ToId,
        string Label,
        bool IsWaitEdge);

    public sealed class DeadlockGraphEdgeService
    {
        public IReadOnlyList<DeadlockGraphEdge> BuildEdges(
            IEnumerable<DeadlockGraphResourceNode> resources)
        {
            ArgumentNullException.ThrowIfNull(resources);

            var edges = new List<DeadlockGraphEdge>();

            foreach (DeadlockGraphResourceNode resource in resources)
            {
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
