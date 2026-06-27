using System;
using System.Collections.Generic;
using System.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphMissingIndexNodeInfo(
        string TableName);

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
            if (string.IsNullOrEmpty(node.TableName))
            {
                return null;
            }

            string cleanNodeTable = CleanTableName(node.TableName);
            return suggestions.FirstOrDefault(suggestion =>
                string.Equals(
                    CleanTableName(suggestion.Table),
                    cleanNodeTable,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string CleanTableName(string tableName)
        {
            return tableName.Trim('[', ']');
        }
    }
}
