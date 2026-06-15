using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Scoring
{
    public static class IndexScoringCalculator
    {
        public static void CalculateScore(MissingIndexSuggestion suggestion, XDocument? planDoc, XNamespace? ns)
        {
            if (suggestion == null) return;

            // 1. Extract predicates, sort columns, and output columns from the query plan
            var eqPredCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ineqPredCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderByCols = new List<string>();
            var outputCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (planDoc != null && ns != null)
            {
                string normTable = (suggestion.Table ?? "").Trim('[', ']');

                // Extract OrderBy columns from any Sort operator in the plan
                foreach (var sort in planDoc.Descendants(ns + "Sort"))
                {
                    var orderBy = sort.Element(ns + "OrderBy");
                    if (orderBy != null)
                    {
                        foreach (var colRef in orderBy.Descendants(ns + "ColumnReference"))
                        {
                            string colName = colRef.Attribute("Column")?.Value ?? "";
                            if (!string.IsNullOrEmpty(colName))
                            {
                                orderByCols.Add(colName.Trim('[', ']'));
                            }
                        }
                    }
                }

                // Extract output list and predicates from operators referencing the target table
                foreach (var relOp in planDoc.Descendants(ns + "RelOp"))
                {
                    var obj = relOp.Descendants(ns + "Object").FirstOrDefault();
                    if (obj != null && string.Equals(obj.Attribute("Table")?.Value?.Trim('[', ']'), normTable, StringComparison.OrdinalIgnoreCase))
                    {
                        // Output columns
                        var outputList = relOp.Element(ns + "OutputList");
                        if (outputList != null)
                        {
                            foreach (var colRef in outputList.Descendants(ns + "ColumnReference"))
                            {
                                string colName = colRef.Attribute("Column")?.Value ?? "";
                                if (!string.IsNullOrEmpty(colName))
                                {
                                    outputCols.Add(colName.Trim('[', ']'));
                                }
                            }
                        }

                        // Predicates
                        var preds = relOp.Descendants(ns + "ScalarOperator")
                                         .Select(so => so.Attribute("ScalarString")?.Value)
                                         .Where(s => !string.IsNullOrEmpty(s))
                                         .Select(s => s!)
                                         .ToList();

                        foreach (var p in preds)
                        {
                            string normP = p.Replace("[", "").Replace("]", "").ToLowerInvariant();

                            foreach (var keyCol in (suggestion.KeyColumns ?? Enumerable.Empty<IndexColumn>()).Concat(suggestion.IncludeColumns ?? Enumerable.Empty<IndexColumn>()))
                            {
                                if (keyCol == null || string.IsNullOrEmpty(keyCol.Name)) continue;
                                string colName = keyCol.Name.Trim('[', ']').ToLowerInvariant();

                                if (normP.Contains(colName))
                                {
                                    if (normP.Contains(colName + " =") || normP.Contains("= " + colName) || normP.Contains(colName + " is null"))
                                    {
                                        eqPredCols.Add(keyCol.Name.Trim('[', ']'));
                                    }
                                    else if (normP.Contains(colName + " >") || normP.Contains(colName + " <") || 
                                             normP.Contains(colName + " >=") || normP.Contains(colName + " <=") || 
                                             normP.Contains(colName + " <>") || normP.Contains(colName + " like") || 
                                             normP.Contains(colName + " between"))
                                    {
                                        ineqPredCols.Add(keyCol.Name.Trim('[', ']'));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 2. Score(I) = S_eq + S_ineq + S_order + S_cover - S_penalty

            var keyCols = suggestion.KeyColumns;

            // S_eq: Equality Score
            int seq = 0;
            if (keyCols != null)
            {
                for (int i = 0; i < keyCols.Count; i++)
                {
                    var col = keyCols[i];
                    if (col == null || string.IsNullOrEmpty(col.Name)) continue;
                    string colName = col.Name.Trim('[', ']');

                    bool isEquality = (planDoc != null) ? eqPredCols.Contains(colName) : (col.Usage == "EQUALITY");
                    if (isEquality)
                    {
                        seq += 30;
                    }
                    else
                    {
                        break; // Must be contiguous leading key columns
                    }
                }
            }

            // S_ineq: Inequality Score
            int sineq = 0;
            int firstNonEqIndex = 0;
            if (keyCols != null)
            {
                for (int i = 0; i < keyCols.Count; i++)
                {
                    var col = keyCols[i];
                    if (col == null || string.IsNullOrEmpty(col.Name)) continue;
                    string colName = col.Name.Trim('[', ']');

                    bool isEquality = (planDoc != null) ? eqPredCols.Contains(colName) : (col.Usage == "EQUALITY");
                    if (!isEquality)
                    {
                        firstNonEqIndex = i;
                        break;
                    }
                    firstNonEqIndex = i + 1;
                }
            }

            if (keyCols != null && firstNonEqIndex < keyCols.Count)
            {
                var col = keyCols[firstNonEqIndex];
                if (col != null && !string.IsNullOrEmpty(col.Name))
                {
                    string colName = col.Name.Trim('[', ']');
                    bool isInequality = (planDoc != null) ? ineqPredCols.Contains(colName) : (col.Usage == "INEQUALITY");
                    if (isInequality)
                    {
                        sineq = 15;
                    }
                }
            }

            // S_order: OrderBy Score
            int sorder = 0;
            if (orderByCols.Count > 0 && keyCols != null && keyCols.Count > 0)
            {
                int keyIdx = 0;
                while (keyIdx < keyCols.Count)
                {
                    var col = keyCols[keyIdx];
                    if (col == null || string.IsNullOrEmpty(col.Name)) break;
                    string colName = col.Name.Trim('[', ']');

                    bool isEquality = (planDoc != null) ? eqPredCols.Contains(colName) : (col.Usage == "EQUALITY");
                    if (!isEquality) break;
                    keyIdx++;
                }

                if (keyIdx < keyCols.Count && (keyCols.Count - keyIdx) >= orderByCols.Count)
                {
                    bool match = true;
                    for (int o = 0; o < orderByCols.Count; o++)
                    {
                        var col = keyCols[keyIdx + o];
                        if (col == null || string.IsNullOrEmpty(col.Name) || 
                            !string.Equals(col.Name.Trim('[', ']'), orderByCols[o], StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        sorder = 15;
                    }
                }
            }

            // S_cover: Coverage Score
            int scover = 40; // Default covering index value
            if (planDoc != null && outputCols.Count > 0)
            {
                var indexCols = (suggestion.KeyColumns ?? Enumerable.Empty<IndexColumn>())
                                    .Concat(suggestion.IncludeColumns ?? Enumerable.Empty<IndexColumn>())
                                    .Where(c => c != null && !string.IsNullOrEmpty(c.Name))
                                    .Select(c => c.Name.Trim('[', ']'))
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                int covered = outputCols.Count(col => indexCols.Contains(col));
                scover = (int)Math.Round(40.0 * covered / outputCols.Count);
            }

            // S_penalty: Penalty for too many columns
            int keyCount = suggestion.KeyColumns?.Count ?? 0;
            int includeCount = suggestion.IncludeColumns?.Count ?? 0;
            int penalty = Math.Max(0, keyCount - 4) * 2 + Math.Max(0, includeCount - 8) * 2;

            int finalScore = seq + sineq + sorder + scover - penalty;
            if (finalScore > 100) finalScore = 100;
            if (finalScore < 0) finalScore = 0;

            suggestion.Score = finalScore;
        }
    }
}
