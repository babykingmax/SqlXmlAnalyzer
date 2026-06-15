using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class SargableIndexRecommendationRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_035_SARGABLE_INDEX_RECOMMENDATION";
        public string Name => "Sargable Index Recommendation";
        public string Description => "Correlates execution plan scan nodes with high-performance index suggestions and non-SARGable warnings from refactored T-SQL.";

        private static readonly ConditionalWeakTable<XElement, StatementAnalysisState> _analysisCache = new();

        private class StatementAnalysisState
        {
            public bool IsAnalyzed { get; set; }
            public List<MissingIndexSuggestion> Suggestions { get; set; } = new();
            public List<NonSargableExpressionInfo> NonSargableExpressions { get; set; } = new();
        }

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                var physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                // Node-level analysis for Scan/Seek/Lookup operators
                if (nodeId != "0")
                {
                    var stmtSimple = relOp.Ancestors(ns + "StmtSimple").FirstOrDefault();
                    if (stmtSimple == null) return null;

                    var state = _analysisCache.GetOrCreateValue(stmtSimple);
                    EnsureStatementAnalyzed(stmtSimple, state, doc, ns);

                    bool isScanOrLookup = physOp.Contains("Scan") || physOp.Contains("Seek") || physOp.Contains("Lookup");
                    if (!isScanOrLookup) return null;

                    var (schema, table) = GetReferencedTable(relOp, ns);
                    if (string.IsNullOrEmpty(table)) return null;

                    // Match missing index suggestions for this table
                    var matchedSug = state.Suggestions.FirstOrDefault(s =>
                        string.Equals(s.Table.Trim('[', ']'), table, StringComparison.OrdinalIgnoreCase));

                    // Match non-SARGable expressions referencing columns handled by this RelOp
                    var referencedColumns = relOp.Descendants(ns + "ColumnReference")
                        .Select(c => c.Attribute("Column")?.Value?.Trim('[', ']'))
                        .Where(col => !string.IsNullOrEmpty(col))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var matchedNonSargable = state.NonSargableExpressions.Where(nsInfo =>
                    {
                        if (string.IsNullOrEmpty(nsInfo.ColumnName)) return false;
                        var targets = nsInfo.ColumnName.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                                        .Select(c => c.Trim('[', ']'));
                        return targets.Any(target => referencedColumns.Contains(target));
                    }).ToList();

                    var messages = new List<string>();
                    if (matchedSug != null)
                    {
                        string dbaTip = matchedSug.IncludeColumns.Count > 0 ? "\n   [DBA 提示] 包含 (INCLUDE) 列的总长度在某些 SQL Server 版本中受 1023 字节或 32 个列 of constraints, 请视情况裁剪。" : "";
                        messages.Add($"⭐ **[智能索引推荐]** 评分: {matchedSug.Score}/100 | 建议为表 [{schema}].[{table}] 创建非聚集索引以消除该 {physOp} 节点的开销：\n   👉 {matchedSug.CreateIndexStatement}{dbaTip}");
                    }

                    foreach (var nsInfo in matchedNonSargable)
                    {
                        messages.Add($"❌ **[非 SARGable 表达式警告]** 节点中存在无法被自动优化改写的表达式：\n   - {nsInfo.Description}");
                    }

                    if (messages.Any())
                    {
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Warning",
                            Title = "智能索引与 T-SQL 关联建议",
                            Message = string.Join("|||", messages),
                            NodeId = nodeId
                        };
                    }
                }
                else
                {
                    // Root-level summary of all suggestions and non-SARGable expressions across all statements
                    var allSuggestions = new List<MissingIndexSuggestion>();
                    var allNonSargable = new List<NonSargableExpressionInfo>();

                    var stmtSimples = doc.Descendants(ns + "StmtSimple").ToList();
                    foreach (var stmtSimple in stmtSimples)
                    {
                        var state = _analysisCache.GetOrCreateValue(stmtSimple);
                        EnsureStatementAnalyzed(stmtSimple, state, doc, ns);
                        allSuggestions.AddRange(state.Suggestions);
                        allNonSargable.AddRange(state.NonSargableExpressions);
                    }

                    var summaryMsgs = new List<string>();
                    if (allSuggestions.Any())
                    {
                        summaryMsgs.Add("💡 **[全局智能索引推荐]**");
                        var uniqueSuggestions = allSuggestions
                            .GroupBy(s => s.CreateIndexStatement)
                            .Select(g => g.First())
                            .ToList();

                        foreach (var sug in uniqueSuggestions)
                        {
                            summaryMsgs.Add($"评分: {sug.Score}/100 | 预估提升: {sug.Impact:F1}% | 表: [{sug.Schema}].[{sug.Table}]\n👉 {sug.CreateIndexStatement}");
                        }
                    }

                    if (allNonSargable.Any())
                    {
                        summaryMsgs.Add("❌ **[无法自动改写的非 SARGable 表达式]**");
                        var uniqueNonSargable = allNonSargable
                            .GroupBy(e => e.ExpressionText)
                            .Select(g => g.First())
                            .ToList();

                        foreach (var nsInfo in uniqueNonSargable)
                        {
                            summaryMsgs.Add($"- 风险评分: {nsInfo.RiskScore} | 表达式: `{nsInfo.ExpressionText}`\n  {nsInfo.Description}");
                        }
                    }

                    if (summaryMsgs.Any())
                    {
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Warning",
                            Title = "智能索引与 T-SQL 关联汇总",
                            Message = string.Join("|||", summaryMsgs),
                            NodeId = "0"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"SargableIndexRecommendationRule failed on Node {relOp.Attribute("NodeId")?.Value}: {ex.Message}");
            }

            return null;
        }

        private static void EnsureStatementAnalyzed(XElement stmtSimple, StatementAnalysisState state, XDocument doc, XNamespace ns)
        {
            if (state.IsAnalyzed) return;
            state.IsAnalyzed = true;

            string statementText = stmtSimple.Attribute("StatementText")?.Value ?? "";
            if (!string.IsNullOrEmpty(statementText))
            {
                try
                {
                    // Refactor query using the refactoring engine
                    var refactorEngine = new SqlRefactorEngine(registerCoreRules: true, registerLegacyRules: true);
                    string refactoredSql = refactorEngine.Refactor(statementText, out var errors);

                    // Suggest indexes on the refactored SQL
                    state.Suggestions = MissingIndexSuggester.SuggestIndexes(refactoredSql);

                    // Calculate scores using the actual plan document
                    foreach (var sug in state.Suggestions)
                    {
                        SqlXmlAnalyzer.Core.Scoring.IndexScoringCalculator.CalculateScore(sug, doc, ns);
                    }

                    // Detect non-SARGable expressions
                    state.NonSargableExpressions = NonSargableDetector.Detect(refactoredSql);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"SargableIndexRecommendationRule initialization failed: {ex.Message}");
                }
            }
        }

        private static (string schema, string table) GetReferencedTable(XElement relOp, XNamespace ns)
        {
            var objEl = relOp.Descendants(ns + "Object").FirstOrDefault();
            if (objEl != null)
            {
                string schema = objEl.Attribute("Schema")?.Value?.Trim('[', ']') ?? "dbo";
                string table = objEl.Attribute("Table")?.Value?.Trim('[', ']') ?? "";
                return (schema, table);
            }
            return ("", "");
        }
    }
}
