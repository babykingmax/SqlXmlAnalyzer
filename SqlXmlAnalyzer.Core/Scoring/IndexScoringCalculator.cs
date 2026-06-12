using System;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Scoring
{
    public static class IndexScoringCalculator
    {
        public static void CalculateScore(MissingIndexSuggestion suggestion, XDocument planDoc, XNamespace ns)
        {
            double coverage = CalculateCoverage(suggestion, planDoc, ns);
            double seekability = CalculateSeekability(suggestion);
            double included = CalculateIncludedFactor(suggestion);
            double uniqueness = CalculateUniqueness(suggestion, planDoc, ns);

            double total = coverage * 0.4 + seekability * 0.3 + included * 0.2 + uniqueness * 0.1;
            suggestion.Score = (int)Math.Round(total * 100);
            if (suggestion.Score > 100) suggestion.Score = 100;
            if (suggestion.Score < 0) suggestion.Score = 0;
        }

        private static double CalculateCoverage(MissingIndexSuggestion suggestion, XDocument planDoc, XNamespace ns)
        {
            // A simplified heuristic: coverage is good if we include many columns.
            // Ideally we'd extract all columns from the plan, but as a heuristic:
            int totalCols = suggestion.KeyColumns.Count + suggestion.IncludeColumns.Count;
            if (totalCols >= 3) return 1.0;
            if (totalCols == 2) return 0.8;
            if (totalCols == 1) return 0.5;
            return 0.1;
        }

        private static double CalculateSeekability(MissingIndexSuggestion suggestion)
        {
            int eqCols = suggestion.KeyColumns.Count(c => c.Usage == "EQUALITY");
            double score = Math.Min(1.0, eqCols / 3.0);
            
            if (suggestion.KeyColumns.Count > 0 && suggestion.KeyColumns.First().Usage == "EQUALITY")
            {
                score += 0.2;
            }
            return Math.Min(1.0, score);
        }

        private static double CalculateIncludedFactor(MissingIndexSuggestion suggestion)
        {
            int incCount = suggestion.IncludeColumns.Count;
            if (incCount == 0) return 0.5; // No includes might mean we do a lookup, neutral.
            return Math.Min(1.0, incCount / 5.0);
        }

        private static double CalculateUniqueness(MissingIndexSuggestion suggestion, XDocument planDoc, XNamespace ns)
        {
            // If planDoc is null (e.g. from Sandbox testing mode), return a heuristic default.
            if (planDoc == null) return 0.7;
            
            // For now, heuristic default. Later, could inspect missing index element details.
            return 0.7;
        }
    }
}
