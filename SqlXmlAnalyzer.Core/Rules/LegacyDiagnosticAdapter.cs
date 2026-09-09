using System.Globalization;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Rules;

internal static class LegacyDiagnosticAdapter
{
    public static RuleEvaluation Evaluate(IPlanAnalyzerRule rule, RuleAnalysisContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Metadata.Scope == RuleScope.Statement
            && !context.LegacyElement.AncestorsAndSelf(context.Namespace + "StmtSimple").Any())
            return RuleEvaluation.Skipped("RULE_NOT_APPLICABLE", "兼容语句规则仅支持 StmtSimple。");
        RuleEvaluation? skipped = CheckEvidence(rule, context);
        if (skipped != null) return skipped;
        AnalysisResult? result = rule.Analyze(context.LegacyElement, context.Namespace);
        context.CancellationToken.ThrowIfCancellationRequested();
        if (result == null) return RuleEvaluation.NoHit("兼容规则已执行，未返回匹配结果；不表示未实现的检查也已通过。");
        if (!IsSupportedResultId(rule, context.Metadata.RuleId, result.RuleId))
            throw new InvalidOperationException("RULE_RESULT_ID_MISMATCH: 规则返回了不属于自身的 RuleId。");
        var location = context.LocationFor(result.ResultScope ?? context.Metadata.Scope);
        var proposal = new DiagnosticProposal(result.RuleId, result.Title, "兼容规则报告了一个候选诊断。", ParseSeverity(result.Severity))
        {
            Scope = result.ResultScope, LegacyResult = result, LegacyExplanation = result.Message,
            Evidence = new[] { new DiagnosticEvidence("SourceScope", location.Source.XmlPath, "XML scope", location) },
            Applicability = new[] { "当前规则已实现的 XML 检查与配置。" },
            Limitations = new[] { "此规则仍使用旧 XML 适配；所列证据定位调用范围，兼容说明中的根因及建议尚未逐项结构化核验。" }
        };
        if (context.Facts is not { } facts) return RuleEvaluation.Hit(proposal);

        DiagnosticEvidence Metric<T>(string name, PlanMetric<T> metric) where T : struct, IFormattable =>
            new(name, metric.IsAvailable ? metric.Value!.Value.ToString("G", CultureInfo.InvariantCulture) : null,
                metric.Source, location, metric.Unit, metric.State, metric.Kind, metric.Aggregation);
        if (rule is ZeroRowActualsRule)
        {
            proposal = proposal with
            {
                SemanticCode = rule.RuleId + ":rows-per-execution", Confidence = DiagnosticConfidence.High,
                Summary = $"估算每次执行 {facts.EstimatedRows.Display()} 行，实际每次执行 {facts.RowsPerExecution.Display()} 行。",
                Evidence = new[] { Metric("EstimatedRows", facts.EstimatedRows), Metric("OutputRows", facts.OutputRows),
                    Metric("LogicalExecutions", facts.LogicalExecutions), Metric("RowsPerExecution", facts.RowsPerExecution) },
                Hypotheses = new[] { "偏差可能与统计信息、参数或过滤条件有关，现有证据不能确定根因。" },
                Recommendations = new[] { "核对谓词、参数与统计信息，并验证调整前后的执行计划。" },
                Applicability = new[] { "估算行数及每次执行实际行数必须可用；沿用该 RuleId 的原有阈值。" },
                Limitations = new[] { "未据此证明统计信息过时、内存浪费或连接算法选择错误。" },
                LegacyExplanation = null
            };
        }
        return RuleEvaluation.Hit(proposal);
    }

    private static RuleEvaluation? CheckEvidence(IPlanAnalyzerRule rule, RuleAnalysisContext context)
    {
        var facts = context.Facts;
        RuleEvaluation Missing(string fields) => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "缺少可用证据：" + fields);
        if (rule is ZeroRowActualsRule)
            return facts?.EstimatedRows.IsAvailable == true && facts.RowsPerExecution.IsAvailable ? null : Missing("EstimatedRows / RowsPerExecution");
        if (rule is ThreadSkewRule)
        {
            if (facts?.Execution.Parallel == false) return RuleEvaluation.Skipped("RULE_NOT_APPLICABLE", "算子已明确标为串行。");
            return facts?.WorkerRows != null ? null : Missing("完整且角色明确的 worker 行分布");
        }
        if (rule is ParallelSkewRule)
        {
            if (facts?.Execution.Parallel == false) return RuleEvaluation.Skipped("RULE_NOT_APPLICABLE", "算子已明确标为串行。");
            return facts?.Execution.Parallel == true && facts.WorkerRows != null ? null : Missing("Parallel / worker 行分布");
        }
        string physical = context.Operator?.PhysicalOp ?? (string?)context.LegacyElement.Attribute("PhysicalOp") ?? "";
        if (rule is NestedLoopsHighExecRule)
        {
            if (!physical.Contains("Nested Loops") && physical is not "Key Lookup" and not "Clustered Index Seek")
                return RuleEvaluation.Skipped("RULE_NOT_APPLICABLE", "算子不属于本规则支持的循环或查找类型。");
            return facts?.OutputRows.IsAvailable == true && facts.LogicalExecutions.IsAvailable ? null : Missing("OutputRows / LogicalExecutions");
        }
        return null;
    }

    // Historical result branches are not separate registrations/configuration owners.
    // Restrict each exception to the exact built-in implementation and owner so a
    // different legacy rule cannot impersonate another rule's result identifier.
    private static bool IsSupportedResultId(IPlanAnalyzerRule rule, string ownerId, string resultId) =>
        string.Equals(ownerId, resultId, StringComparison.Ordinal) || (ownerId, resultId) switch
        {
            ("RULE_009_PARALLEL_SKEW", "RULE_010_INEFFECTIVE_PARALLELISM") => rule.GetType() == typeof(ParallelSkewRule),
            ("RULE_006_RESIDUAL_PREDICATE", "RULE_007_NON_SARGABLE") => rule.GetType() == typeof(ResidualPredicateRule),
            ("RULE_013_ANTI_PATTERN", "RULE_013_CASE_IN_PREDICATE") => rule.GetType() == typeof(AntiPatternRule),
            ("RULE_003_PARAM_SNIFFING", "RULE_003_OPTIMIZE_FOR_UNKNOWN") => rule.GetType() == typeof(ParameterSniffingRule),
            _ => false
        };

    internal static IssueSeverity ParseSeverity(string severity) => severity switch
    {
        "Info" => IssueSeverity.Info, "Warning" => IssueSeverity.Warning, "Critical" => IssueSeverity.Critical,
        _ => throw new InvalidOperationException("RULE_SEVERITY_INVALID: 规则返回了未知严重度。")
    };
}
