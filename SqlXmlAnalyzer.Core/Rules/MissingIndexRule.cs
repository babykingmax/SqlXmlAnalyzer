using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class MissingIndexRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_020_MISSING_INDEX";
        public string Name => "Missing Index Suggestion";
        public string Description => "Extracts and scores missing indexes.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            if (relOp.Attribute("NodeId")?.Value != "0") return null;

            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var missingIndexes = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, ns);
                var messages = new List<string>();

                foreach (var mi in missingIndexes)
                {
                    string dbaTip = mi.IncludeColumns.Count > 0 ? "\n   [DBA 提示] 包含 (INCLUDE) 列的总长度在某些 SQL Server 版本中受 1023 字节或 32 个列的限制，请视情况裁剪。" : "";
                    messages.Add($"⭐ 评分: {mi.Score}/100 | 预估提升: {mi.Impact:F1}% | 推荐覆盖索引建表 DDL:\n   👉 {mi.CreateIndexStatement}{dbaTip}");
                }

                if (messages.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "缺失索引建议与 DDL",
                        Message = string.Join("|||", messages),
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"MissingIndexRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
