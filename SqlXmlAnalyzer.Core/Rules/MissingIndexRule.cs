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
        public RuleMetadata Metadata => RuleMetadataCatalog.Get(RuleId, Description);

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            var doc = relOp.Document;
            if (doc == null) return null;

            var missingIndexes = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, ns);
            var messages = new List<string>();

            foreach (var mi in missingIndexes)
            {
                string impact = mi.CapturedImpact is { } value ? value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%" : "N/A";
                messages.Add($"⭐ 工具评分: {mi.Score}/100 | SQL Server Impact: {impact}（优化器估算） | 对象: {mi.ObjectIdentity?.DisplayName} | 来源: {mi.Location?.DisplayScope}\n评分模型 {mi.ScoreAssessment?.ModelVersion}；{mi.ScoreAssessment?.Breakdown}\n   👉 {mi.CreateIndexStatement}\n既有索引目录、列类型与版本限制未核验，不判断重复或已被覆盖；评分不是实测收益。");
            }

            if (messages.Any())
            {
                return new AnalysisResult
                {
                    RuleId = this.RuleId,
                    Severity = "Warning",
                    Title = "缺失索引建议与 DDL",
                    Message = string.Join("|||", messages),
                    NodeId = string.Empty,
                    ResultScope = RuleScope.Plan
                };
            }

            return null;
        }
    }
}
