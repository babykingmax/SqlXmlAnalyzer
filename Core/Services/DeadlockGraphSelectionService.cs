using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class DeadlockGraphSelectionService
    {
        public LockResource? FindResourceForNode(
            string nodeId,
            IReadOnlyDictionary<string, (string LockType, string ObjectName)> resourceGroupDetails,
            IEnumerable<LockResource>? resources)
        {
            ArgumentNullException.ThrowIfNull(nodeId);
            ArgumentNullException.ThrowIfNull(resourceGroupDetails);

            if (!nodeId.StartsWith("res_single_", StringComparison.Ordinal) ||
                resources == null ||
                !resourceGroupDetails.TryGetValue(nodeId, out var details))
            {
                return null;
            }

            return resources.FirstOrDefault(resource =>
                string.Equals(resource.ObjectName, details.ObjectName, StringComparison.Ordinal) &&
                string.Equals(resource.LockType, details.LockType, StringComparison.Ordinal));
        }

        public DeadlockProcess? FindProcessForNode(
            string nodeId,
            IEnumerable<DeadlockProcess>? processes)
        {
            ArgumentNullException.ThrowIfNull(nodeId);

            if (!nodeId.StartsWith("proc_id_", StringComparison.Ordinal) ||
                processes == null)
            {
                return null;
            }

            string processId = nodeId["proc_id_".Length..];
            return processes.FirstOrDefault(process =>
                string.Equals(process.Id, processId, StringComparison.Ordinal));
        }
    }
}
