using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ResidualPredicateRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_006_RESIDUAL_PREDICATE";
        public string Name => "Scan with Residual Predicate";
        public string Description => "Detects if a Scan or Seek operator has residual predicates that cannot use an index seek.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                if (!physicalOp.Contains("Scan") && !physicalOp.Contains("Seek"))
                    return null;

                // Residual predicates are inside <Predicate> block of IndexScan
                var indexScan = relOp.Element(ns + "IndexScan") ?? relOp.Element(ns + "TableScan");
                if (indexScan == null) return null;

                var predicate = indexScan.Element(ns + "Predicate");
                if (predicate != null)
                {
                    // If it's a Seek, there's usually a SeekPredicates as well. The <Predicate> here acts as a residual filter.
                    string predicateText = GetScalarOperatorString(predicate, ns);

                    // Check for function-wrapped columns (like CONVERT, YEAR, SUBSTRING)
                    bool hasFunctionWrapped = predicateText.Contains("CONVERT") || predicateText.Contains("YEAR") ||
                                              predicateText.Contains("SUBSTRING") || predicateText.Contains("ISNULL");

                    if (hasFunctionWrapped)
                    {
                        return new AnalysisResult
                        {
                            RuleId = "RULE_007_NON_SARGABLE",
                            Severity = "Warning",
                            Title = "非 SARGable 谓词 (函数包裹列)",
                            Message = $"在过滤条件中对列使用了函数操作：\n{predicateText}\n这会导致无法使用索引 Seek (即 Non-SARGable)。建议修改 WHERE 子句，将计算和函数移到等式右侧。",
                            NodeId = nodeId
                        };
                    }
                    else
                    {
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Info",
                            Title = "残差谓词 (Residual Predicate)",
                            Message = $"操作符包含需要在内存中逐行过滤的残差谓词：\n{predicateText}\n这通常意味着底层读取了多余的数据。建议添加 INCLUDE 包含列或优化索引。",
                            NodeId = nodeId
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ResidualPredicateRule failed: {ex.Message}");
            }
            return null;
        }

        private string GetScalarOperatorString(XElement root, XNamespace ns)
        {
            var scalarOps = root.Descendants(ns + "ScalarOperator");
            return string.Join(" AND ", scalarOps.Select(x => x.Attribute("ScalarString")?.Value).Where(x => !string.IsNullOrEmpty(x)));
        }
    }
}
