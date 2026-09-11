using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class SargableIndexRecommendationRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_035_SARGABLE_INDEX_RECOMMENDATION";
        public string Name => "Sargable Index Recommendation";
        public string Description => "Correlates execution plan scan nodes with high-performance index suggestions and non-SARGable warnings from refactored T-SQL.";
        public RuleMetadata Metadata => RuleMetadataCatalog.Get(RuleId, Description);

        public RuleEvaluation Evaluate(RuleAnalysisContext context)
        {
            var statement = context.LegacyElement.AncestorsAndSelf().FirstOrDefault(e => e.Name == context.Namespace + "StmtSimple");
            if (statement?.Document == null) return RuleEvaluation.Skipped("RULE_NOT_APPLICABLE", "索引候选需要可定位的 StmtSimple。");
            var state = _analysisCache.GetValue(statement, CreateStatementState);
            EnsureStatementAnalyzed(statement, state, statement.Document, context.Namespace);
            var proposals = new List<DiagnosticProposal>();
            foreach (var suggestion in state.Suggestions)
            {
                bool bound = suggestion.Location?.QueryPlan != null;
                proposals.Add(new DiagnosticProposal(bound ? "INDEX_SQL_CANDIDATE" : "INDEX_TARGET_UNRESOLVED",
                    "智能索引与 T-SQL 关联建议", bound ? suggestion.CreateIndexStatement : "缺少唯一的完整对象证据，未生成 DDL。", Core.Abstractions.IssueSeverity.Warning)
                {
                    Scope = RuleScope.Statement,
                    Confidence = bound ? DiagnosticConfidence.Medium : DiagnosticConfidence.Low,
                    Evidence = new[] { new DiagnosticEvidence("Target", suggestion.ObjectIdentity?.DisplayName,
                        "StatementText/SchemaObjectName", suggestion.Location ?? context.Location),
                        new DiagnosticEvidence("ScoreModel", suggestion.ScoreAssessment?.ModelVersion, "IndexScoringCalculator", suggestion.Location ?? context.Location),
                        new DiagnosticEvidence("ScoreBreakdown", suggestion.ScoreAssessment?.Breakdown, "IndexScoringCalculator", suggestion.Location ?? context.Location),
                        new DiagnosticEvidence("ScoreWeights", suggestion.ScoreAssessment?.Weights, "IndexScoringCalculator", suggestion.Location ?? context.Location),
                        new DiagnosticEvidence("ScoreInputScope", suggestion.ScoreAssessment?.InputScope, "IndexScoringCalculator", suggestion.Location ?? context.Location),
                        new DiagnosticEvidence("ScoreEvidenceSource", suggestion.ScoreAssessment?.EvidenceSource, "IndexScoringCalculator", suggestion.Location ?? context.Location) }
                        .Concat(suggestion.KeyColumns.Concat(suggestion.IncludeColumns).Select((column, ordinal) =>
                            new DiagnosticEvidence($"Column[{ordinal}]/{column.Usage}", column.Name, "StatementText", suggestion.Location ?? context.Location))).ToArray(),
                    Limitations = ["既有索引目录与列类型未核验，不判断已存在或被覆盖；建议不保证实际收益。",
                        suggestion.ScoreAssessment?.Limitations ?? "评分证据不可用。"]
                });
            }
            foreach (var expression in state.NonSargableExpressions)
                proposals.Add(new DiagnosticProposal("INDEX_NON_SARGABLE_EXPRESSION", "非 SARGable 表达式候选",
                    expression.Description, Core.Abstractions.IssueSeverity.Warning)
                {
                    Scope = RuleScope.Statement, Confidence = DiagnosticConfidence.Low,
                    Evidence = [new("Expression", expression.ExpressionText, "StatementText", context.Location)],
                    Limitations = ["语法候选不证明访问路径或索引收益；需核对实际计划。"]
                });
            return proposals.Count == 0 ? RuleEvaluation.NoHit() : RuleEvaluation.Hit(proposals.ToArray());
        }

        private static readonly ConditionalWeakTable<XElement, StatementAnalysisState> _analysisCache = new();

        private class StatementAnalysisState
        {
            public bool IsAnalyzed { get; set; }
            public System.Runtime.ExceptionServices.ExceptionDispatchInfo? Failure { get; set; }
            public PlanDocument? Model { get; set; }
            public List<MissingIndexSuggestion> Suggestions { get; set; } = new();
            public List<NonSargableExpressionInfo> NonSargableExpressions { get; set; } = new();
        }

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            var doc = relOp.Document;
            if (doc == null) return null;

            var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
            var physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

            // Node-level analysis for Scan/Seek/Lookup operators
            if (physOp.Contains("Scan") || physOp.Contains("Seek") || physOp.Contains("Lookup"))
            {
                var stmtSimple = relOp.Ancestors(ns + "StmtSimple").FirstOrDefault();
                if (stmtSimple == null) return null;

                var state = _analysisCache.GetValue(stmtSimple, CreateStatementState);
                EnsureStatementAnalyzed(stmtSimple, state, doc, ns);

                bool isScanOrLookup = physOp.Contains("Scan") || physOp.Contains("Seek") || physOp.Contains("Lookup");
                if (!isScanOrLookup) return null;

                var matchedSuggestions = state.Suggestions.Where(s =>
                    IndexTargetResolver.FindOperators(s, doc, ns).Contains(relOp)).ToArray();

                // Match non-SARGable expressions referencing columns handled by this RelOp
                var referencedColumns = IndexTargetResolver.LocalElements(relOp, ns).Where(e => e.Name == ns + "ColumnReference")
                    .Select(c => SqlObjectIdentity.DecodeIdentifier((string?)c.Attribute("Column")))
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
                foreach (var matchedSug in matchedSuggestions)
                {
                    messages.Add($"⭐ **[智能索引推荐]** 评分: {matchedSug.Score}/100 | 对象: {matchedSug.ObjectIdentity!.DisplayName}；来源: {matchedSug.Location!.DisplayScope}。候选需核验既有索引、列类型及实际计划，不能保证消除算子开销。\n   👉 {matchedSug.CreateIndexStatement}");
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
                    var state = _analysisCache.GetValue(stmtSimple, CreateStatementState);
                    EnsureStatementAnalyzed(stmtSimple, state, doc, ns);
                    allSuggestions.AddRange(state.Suggestions);
                    allNonSargable.AddRange(state.NonSargableExpressions);
                }

                var summaryMsgs = new List<string>();
                if (allSuggestions.Any())
                {
                    summaryMsgs.Add("💡 **[全局智能索引推荐]**");
                    foreach (var sug in allSuggestions)
                    {
                        string ddl = sug.Location?.QueryPlan == null ? "INDEX_TARGET_UNRESOLVED：缺少唯一的计划对象证据，未生成 DDL。" : sug.CreateIndexStatement;
                        summaryMsgs.Add($"评分: {sug.Score}/100 | 对象: {sug.ObjectIdentity?.DisplayName} | 来源: {sug.Location?.DisplayScope}\n👉 {ddl}\n既有索引目录未核验，不判断重复或已覆盖。");
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
                        NodeId = string.Empty,
                        ResultScope = RuleScope.Plan
                    };
                }
            }

            return null;
        }

        private static StatementAnalysisState CreateStatementState(XElement statement)
        {
            var state = new StatementAnalysisState();
            statement.Changed += (_, _) =>
            {
                state.IsAnalyzed = false;
                state.Failure = null;
                state.Suggestions.Clear();
                state.NonSargableExpressions.Clear();
            };
            return state;
        }

        private static void EnsureStatementAnalyzed(XElement stmtSimple, StatementAnalysisState state, XDocument doc, XNamespace ns)
        {
            var model = PlanIdentityAdapter.GetDocument(doc);
            if (!ReferenceEquals(model, state.Model))
            {
                state.IsAnalyzed = false;
                state.Failure = null;
                state.Suggestions.Clear();
                state.NonSargableExpressions.Clear();
                state.Model = model;
            }
            state.Failure?.Throw();
            if (state.IsAnalyzed) return;

            string statementText = stmtSimple.Attribute("StatementText")?.Value ?? "";
            if (!string.IsNullOrEmpty(statementText))
            {
                try
                {
                    // Refactor query using the refactoring engine
                    var refactorEngine = new SqlRefactorEngine(registerCoreRules: true, registerLegacyRules: true);
                    string refactoredSql = refactorEngine.Refactor(statementText, out var errors);
                    if (errors.Count > 0)
                        throw new RuleSkippedException("RULE_MISSING_EVIDENCE", "语句 SQL 存在语法错误，不能完成索引关联检查。");

                    // Suggest indexes on the refactored SQL
                    state.Suggestions = MissingIndexSuggester.SuggestIndexes(refactoredSql);

                    // Calculate scores using the actual plan document
                    foreach (var sug in state.Suggestions)
                    {
                        IndexTargetResolver.BindSqlSuggestion(sug, stmtSimple, ns);
                        SqlXmlAnalyzer.Core.Scoring.IndexScoringCalculator.CalculateScore(sug, doc, ns);
                    }

                    // Detect non-SARGable expressions
                    state.NonSargableExpressions = NonSargableDetector.Detect(refactoredSql);
                }
                catch (Exception exception)
                {
                    state.Failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
                    throw;
                }
            }
            state.IsAnalyzed = true;
        }

    }
}
