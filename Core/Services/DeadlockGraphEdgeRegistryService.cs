using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class DeadlockGraphEdgeRegistryService
    {
        public IReadOnlyList<DeadlockGraphEdge> FindEdgesForNode(
            IEnumerable<DeadlockGraphEdge> edges,
            string nodeId)
        {
            ArgumentNullException.ThrowIfNull(edges);
            ArgumentNullException.ThrowIfNull(nodeId);

            return edges
                .Where(edge =>
                    string.Equals(edge.FromId, nodeId, StringComparison.Ordinal) ||
                    string.Equals(edge.ToId, nodeId, StringComparison.Ordinal))
                .ToList();
        }

        public bool IsWaitEdge(
            IEnumerable<DeadlockGraphEdge> edges,
            string fromId,
            string toId)
        {
            ArgumentNullException.ThrowIfNull(edges);
            ArgumentNullException.ThrowIfNull(fromId);
            ArgumentNullException.ThrowIfNull(toId);

            DeadlockGraphEdge? edge = edges.FirstOrDefault(item =>
                string.Equals(item.FromId, fromId, StringComparison.Ordinal) &&
                string.Equals(item.ToId, toId, StringComparison.Ordinal));

            return edge?.IsWaitEdge ?? true;
        }
    }
}
