using System;
using System.Collections.Generic;
using System.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphMissingIndexNodeInfo(
        string TableName)
    {
        public SqlObjectIdentity? ObjectIdentity { get; init; }
        public PlanQueryPlanKey? QueryPlan { get; init; }
    }

    public sealed class PlanGraphMissingIndexAssociationService
    {
        public IReadOnlyList<MissingIndexSuggestion?> MatchSuggestions(
            IReadOnlyList<PlanGraphMissingIndexNodeInfo> nodes,
            IReadOnlyList<MissingIndexSuggestion> suggestions)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(suggestions);

            return nodes
                .Select(node => MatchSuggestion(node, suggestions))
                .ToList();
        }

        private static MissingIndexSuggestion? MatchSuggestion(
            PlanGraphMissingIndexNodeInfo node,
            IReadOnlyList<MissingIndexSuggestion> suggestions)
        {
            var identity = node.ObjectIdentity;
            if (node.QueryPlan == null || identity?.Database == null || identity.Schema == null || identity.Object == null)
            {
                return null;
            }

            // This is a reference association inside the exact captured QueryPlan,
            // not global object deduplication. Missing Server matches only another
            // locally unspecified Server; it never acts as a wildcard.
            var matches = suggestions.Where(suggestion => suggestion.Location?.QueryPlan == node.QueryPlan &&
                suggestion.ObjectIdentity == identity).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
}
