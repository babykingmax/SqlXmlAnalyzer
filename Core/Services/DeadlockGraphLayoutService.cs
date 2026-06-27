using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockGraphLayout(
        IReadOnlyList<DeadlockGraphProcessNode> Processes,
        IReadOnlyList<DeadlockGraphResourceNode> Resources,
        IReadOnlyDictionary<string, (string LockType, string ObjectName)> ResourceGroupDetails);

    public sealed class DeadlockGraphProcessNode
    {
        public string Spid { get; init; } = "";
        public string PrimaryId { get; init; } = "";
        public int ThreadCount { get; init; }
        public IReadOnlyList<DeadlockProcess> Threads { get; init; } = Array.Empty<DeadlockProcess>();
        public DeadlockProcess PrimaryProcess => Threads.FirstOrDefault(t => t.Ecid == "0") ?? Threads.First();
    }

    public sealed class DeadlockGraphResourceNode
    {
        public string Id { get; init; } = "";
        public string LockType { get; init; } = "";
        public string ObjectName { get; init; } = "";
        public string IndexName { get; init; } = "";
        public int LockCount { get; init; }
        public IReadOnlyList<LockResource> RawResources { get; init; } = Array.Empty<LockResource>();
        public string Dbid => RawResources.FirstOrDefault()?.Dbid ?? "";

        public IReadOnlySet<string> OwnerSpids { get; init; } = new HashSet<string>();
        public IReadOnlySet<string> WaiterSpids { get; init; } = new HashSet<string>();

        public string OwnerModes => string.Join(
            ", ",
            RawResources.SelectMany(resource => resource.Owners).Select(owner => owner.Mode).Distinct());

        public string WaiterModes => string.Join(
            ", ",
            RawResources.SelectMany(resource => resource.Waiters).Select(waiter => waiter.Mode).Distinct());
    }

    public sealed class DeadlockGraphLayoutService
    {
        public DeadlockGraphLayout BuildLayout(DeadlockGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            List<DeadlockGraphProcessNode> processes = graph.Processes
                .DistinctBy(process => process.Id)
                .Select(process => new DeadlockGraphProcessNode
                {
                    Spid = process.Spid,
                    PrimaryId = process.Id,
                    ThreadCount = 1,
                    Threads = new[] { process }
                })
                .ToList();

            List<DeadlockGraphResourceNode> resources = graph.Resources
                .Select((resource, index) => CreateResourceNode(resource, index))
                .ToList();

            Dictionary<string, (string LockType, string ObjectName)> resourceGroupDetails = resources
                .ToDictionary(
                    resource => resource.Id,
                    resource => (resource.LockType, resource.ObjectName),
                    StringComparer.Ordinal);

            return new DeadlockGraphLayout(processes, resources, resourceGroupDetails);
        }

        private static DeadlockGraphResourceNode CreateResourceNode(LockResource resource, int index)
        {
            return new DeadlockGraphResourceNode
            {
                Id = $"res_single_{index}",
                LockType = resource.LockType,
                ObjectName = resource.ObjectName,
                IndexName = resource.IndexName,
                LockCount = 1,
                RawResources = new[] { resource },
                OwnerSpids = resource.Owners.Select(owner => owner.Id).ToHashSet(StringComparer.Ordinal),
                WaiterSpids = resource.Waiters.Select(waiter => waiter.Id).ToHashSet(StringComparer.Ordinal)
            };
        }
    }
}
