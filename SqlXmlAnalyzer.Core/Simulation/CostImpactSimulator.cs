using System;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Simulation
{
    public static class CostImpactSimulator
    {
        private const double ConvertToSeekReductionRatio = 0.6;
        private const double EliminateLookupReductionRatio = 0.4;
        private const double EliminateSortReductionRatio = 0.3;
        private const double FilterPushdownReductionRatio = 0.2;

        public static CostImpactResult Simulate(XDocument? originalPlan, MissingIndexSuggestion proposedIndex, XNamespace ns)
        {
            if (originalPlan == null || proposedIndex == null || !proposedIndex.KeyColumns.Any())
                return new CostImpactResult(0, "无足够数据评估成本影响。");

            var statements = originalPlan.Descendants(ns + "StmtSimple");
            double totalReduction = 0.0;
            double totalOriginalCost = 0.0;
            int affectedOpsCount = 0;

            foreach (var stmt in statements)
            {
                var stmtCostAttr = stmt.Attribute("StatementSubTreeCost");
                if (stmtCostAttr != null && NumericParser.TryParseInvariantDouble(stmtCostAttr.Value, out double stmtCost))
                {
                    totalOriginalCost += stmtCost;
                }

                var relOps = stmt.Descendants(ns + "RelOp");
                foreach (var op in relOps)
                {
                    var tableElement = op.Descendants(ns + "Object").FirstOrDefault();
                    if (tableElement != null)
                    {
                        var tableAttr = tableElement.Attribute("Table");
                        if (tableAttr != null && tableAttr.Value.Equals(proposedIndex.Table, StringComparison.OrdinalIgnoreCase))
                        {
                            double operatorCost = 0.0;
                            var costAttr = op.Attribute("EstimatedTotalSubtreeCost");
                            if (costAttr != null && NumericParser.TryParseInvariantDouble(costAttr.Value, out operatorCost))
                            {
                                // Prevent divide by zero if totalOriginalCost is missing or 0
                                double operatorCostRatio = totalOriginalCost > 0 ? operatorCost / totalOriginalCost : 0;
                                double reductionRatio = GetReductionRatio(op, proposedIndex, ns);
                                if (reductionRatio > 0)
                                {
                                    totalReduction += operatorCostRatio * reductionRatio;
                                    affectedOpsCount++;
                                }
                            }
                        }
                    }
                }
            }

            int reductionPercent = (int)Math.Round(totalReduction * 100);
            reductionPercent = Math.Min(100, Math.Max(0, reductionPercent));

            string description;
            if (reductionPercent > 0)
            {
                description = $"新索引可优化 {affectedOpsCount} 个操作符，预计将扫描转换为更高效的查找操作或消除回表。";
            }
            else
            {
                description = "当前索引定义对计划中现有操作符的影响较小，或表名不匹配。";
            }

            return new CostImpactResult(reductionPercent, description);
        }

        private static double GetReductionRatio(XElement op, MissingIndexSuggestion index, XNamespace ns)
        {
            var physicalOp = op.Attribute("PhysicalOp")?.Value;
            var logicalOp = op.Attribute("LogicalOp")?.Value;

            if (physicalOp == "Table Scan" || physicalOp == "Clustered Index Scan" || physicalOp == "Index Scan")
            {
                if (CanConvertToSeek(op, index, ns))
                    return ConvertToSeekReductionRatio;
            }
            else if (logicalOp == "RID Lookup" || physicalOp == "Index Seek" && logicalOp == "Key Lookup") // Sometimes Key Lookup has PhysicalOp Index Seek and LogicalOp Key Lookup
            {
                if (IsCoveringIndex(op, index, ns))
                    return EliminateLookupReductionRatio;
            }
            else if (physicalOp == "Sort")
            {
                if (CanEliminateSort(op, index, ns))
                    return EliminateSortReductionRatio;
            }

            return 0;
        }

        private static bool CanConvertToSeek(XElement scanOp, MissingIndexSuggestion index, XNamespace ns)
        {
            // Simplified check: If it's a scan on the target table, and our index has keys,
            // we assume it can be converted to a seek if the plan originally missed an index here.
            // For a robust simulator, we would check the SeekPredicates or Predicates to match column names.
            return index.KeyColumns.Any();
        }

        private static bool IsCoveringIndex(XElement lookupOp, MissingIndexSuggestion index, XNamespace ns)
        {
            // For a lookup, we check if the columns being fetched are included in our index (either as keys or includes).
            // Simplified logic: Assume it covers if includes are present.
            return index.IncludeColumns.Any();
        }

        private static bool CanEliminateSort(XElement sortOp, MissingIndexSuggestion index, XNamespace ns)
        {
            // If it's a sort on the target table (rarely directly tied to table object, but just in case)
            // assume it can eliminate sort if the first key column matches the sort order.
            return index.KeyColumns.Count > 0;
        }
    }
}
