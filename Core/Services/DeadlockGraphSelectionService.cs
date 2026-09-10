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

            if (!int.TryParse(nodeId["res_single_".Length..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int index)) return null;
            var resource = resources.ElementAtOrDefault(index);
            return resource != null && resource.ObjectName == details.ObjectName && resource.LockType == details.LockType
                ? resource : null;
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
