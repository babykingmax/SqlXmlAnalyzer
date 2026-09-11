using System.Globalization;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Rules;

/// <summary>One measurement and evidence contract for the four cardinality/residual rule entry points.</summary>
internal static class RowCountRuleEvaluator
{
    internal const string CardinalityCode = "CARDINALITY_ROWS_PER_EXECUTION";
    internal const string ResidualReadCode = "RESIDUAL_READ_AMPLIFICATION";

    internal static RuleEvaluation Evaluate(RuleAnalysisContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var result = Evaluate(context.Metadata.RuleId, context.Facts, context.Location,
            context.Operator?.PhysicalOp ?? context.Facts?.PhysicalOp ?? "", context.CancellationToken);
        context.CancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    // Direct XML callers retain their single-result API. The engine calls Evaluate instead,
    // and owns exception capture/Failed publication at its existing execution boundary.
    internal static AnalysisResult? Analyze(string ruleId, XElement relOp, XNamespace ns)
    {
        try
        {
            var facts = PlanOperatorFactsService.Get(relOp, ns);
            var location = PlanIdentityAdapter.GetOperator(relOp)?.Location
                ?? new PlanLocation("legacy-node", null, null, null, null, new DocumentSource(default).Location(relOp));
            var result = Evaluate(ruleId, facts, location, (string?)relOp.Attribute("PhysicalOp") ?? "", default);
            var proposal = result.Diagnostics.SingleOrDefault();
            return proposal == null ? null : new AnalysisResult
            {
                RuleId = proposal.LegacyResult?.RuleId ?? ruleId,
                Severity = proposal.Severity.ToString(), Title = proposal.Title,
                Message = string.Join(Environment.NewLine, new[] { proposal.Summary }
                    .Concat(proposal.Evidence.Where(e => e.Name is "ResidualPredicate" or "SeekPredicate")
                        .Select(e => $"{e.Name}: {e.Value ?? "N/A"}"))
                    .Concat(proposal.Hypotheses).Concat(proposal.Recommendations).Concat(proposal.Limitations)),
                NodeId = (string?)relOp.Attribute("NodeId") ?? "N/A", Location = location
            };
        }
        catch (Exception exception)
        {
            ExceptionPolicy.Describe(exception, $"IMP-14:{ruleId}.Analyze");
            throw;
        }
    }

    private static RuleEvaluation Evaluate(string ruleId, PlanOperatorFacts? facts, PlanLocation location,
        string physicalOp, CancellationToken token)
    {
        Logger.Debug($"IMP-14: 开始计算 {ruleId}。");
        var result = ruleId switch
        {
            "RULE_004_ESTIMATE_MISMATCH" => Cardinality(facts, location, detailed: false),
            "RULE_030_CARDINALITY_ERROR" => Cardinality(facts, location, detailed: true),
            "RULE_006_RESIDUAL_PREDICATE" => Residual(facts, location, physicalOp, requireReadAmplification: false, token),
            "RULE_034_RESIDUAL_PRED_OP" => Residual(facts, location, physicalOp, requireReadAmplification: true, token),
            _ => throw new InvalidOperationException("IMP14_RULE_NOT_SUPPORTED")
        };
        if (result.Status == RuleRunStatus.Skipped)
            Logger.Warning($"IMP-14: {ruleId} 跳过；{result.ReasonCode}。");
        else Logger.Debug($"IMP-14: {ruleId} 完成；{result.Status}。");
        return result;
    }

    private static RuleEvaluation Cardinality(PlanOperatorFacts? facts, PlanLocation location, bool detailed)
    {
        if (facts?.EstimatedRows.IsAvailable != true || !facts.RowsPerExecution.IsAvailable)
            return Missing("EstimatedRows / RowsPerExecution（需要明确的逻辑执行次数）");
        double estimated = facts.EstimatedRows.Value!.Value;
        double actual = (double)facts.RowsPerExecution.Value!.Value;
        // Preserve the historical floor of one row, including zero/fractional estimates.
        // Actual total / common logical executions is calculated centrally by IMP-11;
        // worker execution counters must never be summed to obtain this denominator.
        double denominator = Math.Max(1, Math.Min(estimated, actual));
        double ratio = Math.Max(estimated, actual) / denominator;
        double difference = Math.Abs(estimated - actual);
        IssueSeverity? severity = detailed
            ? (Math.Max(estimated, actual) > 100 && ratio > 10 && difference > 1000 ? IssueSeverity.Critical : null)
            : actual <= 100 && estimated >= 1000 ? IssueSeverity.Critical
            : Math.Max(estimated, actual) < 100 ? null
            : ratio >= 100 ? IssueSeverity.Critical : ratio >= 10 ? IssueSeverity.Warning : null;
        if (severity == null) return RuleEvaluation.NoHit();

        var evidence = new List<DiagnosticEvidence>
        {
            Metric("EstimatedRows", facts.EstimatedRows, location), Metric("OutputRows", facts.OutputRows, location),
            Metric("LogicalExecutions", facts.LogicalExecutions, location), Metric("RowsPerExecution", facts.RowsPerExecution, location),
            Number("ComparisonDenominator", denominator, "max(1, min(EstimatedRows, RowsPerExecution))", "rows/execution", location),
            Number("DeviationRatio", ratio, "max(EstimatedRows, RowsPerExecution) / ComparisonDenominator", "ratio", location),
            Number("AbsoluteRowDifference", difference, "abs(EstimatedRows - RowsPerExecution)", "rows/execution", location)
        };
        AddPredicates(evidence, facts, location);
        return RuleEvaluation.Hit(new DiagnosticProposal(CardinalityCode, "基数估计偏差",
            FormattableString.Invariant($"估算每次执行 {estimated:#,0.################} 行，实际每次执行 {actual:#,0.################} 行；偏差指标 {ratio:G6} 倍（较小行数按至少 1 行计算）。"), severity.Value)
        {
            Confidence = DiagnosticConfidence.High, Evidence = evidence,
            Hypotheses = ["偏差可能与统计信息、参数分布、列相关性或过滤表达式有关，现有证据不能确定根因。"],
            Recommendations = ["核对同次采集的谓词、参数与统计信息，再比较调整前后的实际计划。"],
            Applicability = [detailed
                ? "RULE030：较大单次行数 > 100、偏差指标 > 10 且绝对差值 > 1000 时为 Critical。"
                : "RULE004：较大单次行数至少 100；偏差指标 >= 10 为 Warning、>= 100 为 Critical；估算 >= 1000 且实际 <= 100 时为 Critical。"],
            Limitations = ["实际总输出除以逻辑执行次数；并行 worker 次数不相加，次数不一致时不比较。",
                "分母下限为 1，零行与不足 1 行时该指标不是未经调整的数学倍数；不能据此断定统计信息过时或连接算法错误。"]
        });
    }

    private static RuleEvaluation Residual(PlanOperatorFacts? facts, PlanLocation location, string physicalOp,
        bool requireReadAmplification, CancellationToken token)
    {
        if (!IsScanOrSeek(physicalOp))
            return RuleEvaluation.Skipped("RULE_NOT_APPLICABLE", "规则仅适用于支持的 Scan / Seek 算子。");
        if (facts == null) return Missing("本算子事实");
        if (facts.Predicates.Count == 0)
            return facts.HasResidualPredicate ? Missing("残差谓词 ScalarString") : RuleEvaluation.NoHit();

        bool complete = facts.OutputRows.IsAvailable && facts.RowsRead.IsAvailable;
        bool consistent = complete && facts.RowsRead.Value >= facts.OutputRows.Value
            && !facts.Threads.Any(t => t.RowsRead.Value < t.OutputRows.Value
                || t.Executions.Value == 0 && (t.OutputRows.Value > 0 || t.RowsRead.Value > 0));
        if (requireReadAmplification && !complete) return Missing("OutputRows / RowsRead");
        if (requireReadAmplification && !consistent)
            return RuleEvaluation.Skipped("RULE_INCONSISTENT_EVIDENCE", "读取少于输出，或零执行与非零行数矛盾，不能确认读取放大。");
        decimal output = facts.OutputRows.Value ?? 0;
        decimal read = facts.RowsRead.Value ?? 0;
        bool amplified = consistent && read > output * 1.2m && read - output > 100;
        if (requireReadAmplification && !amplified) return RuleEvaluation.NoHit();

        bool functionOnColumn = PredicateFunctionEvidence.HasFunctionOnColumn(facts.Predicates, token);
        var evidence = new List<DiagnosticEvidence>
        {
            Metric("OutputRows", facts.OutputRows, location), Metric("RowsRead", facts.RowsRead, location)
        };
        AddPredicates(evidence, facts, location);
        if (consistent)
        {
            evidence.Add(Number("RowsReadMinusOutput", read - output, "RowsRead - OutputRows", "rows", location));
            // With zero output a ratio is undefined; keep the actual zero and difference.
            evidence.Add(new("ReadAmplificationRatio", output > 0 ? (read / output).ToString("G", CultureInfo.InvariantCulture) : null,
                "RowsRead / OutputRows", location, "ratio", output > 0 ? PlanMetricState.Available : PlanMetricState.Ambiguous));
        }
        string code = amplified ? ResidualReadCode : functionOnColumn ? "RULE_007_NON_SARGABLE" : "RESIDUAL_PREDICATE_PRESENT";
        string summary = amplified
            ? $"本算子存在残差谓词；实际读取 {facts.RowsRead.Display()} 行，输出 {facts.OutputRows.Display()} 行，差值 {(read - output).ToString(CultureInfo.InvariantCulture)} 行。"
            : "本算子载荷包含残差谓词；尚未确认读取放大。";
        var hypotheses = new List<string> { "残差过滤可能增加读取工作量；读取/输出行数差异不能直接换算为物理 IO 或耗时。" };
        if (functionOnColumn) hypotheses.Add("谓词包含列上的函数调用，可能影响搜索条件；仍需核对数据类型、计算列索引和优化器转换。");
        return RuleEvaluation.Hit(new DiagnosticProposal(code, amplified ? "残差谓词读取放大" : functionOnColumn ? "非 SARGable 谓词候选（列上函数）" : "残差谓词", summary,
            amplified || functionOnColumn ? IssueSeverity.Warning : IssueSeverity.Info)
        {
            Confidence = DiagnosticConfidence.High, Evidence = evidence, Hypotheses = hypotheses,
            Recommendations = ["核对本算子的 Seek 条件、残差条件及现有索引键顺序，再验证候选方案；INCLUDE 仅提供覆盖，不保证消除残差过滤。"],
            Applicability = [amplified ? "本算子 Scan/Seek 带残差，完整读取行数 > 输出行数 × 1.2，且差值 > 100 行。" : "本算子 Scan/Seek 载荷中有可读取的残差谓词。"],
            Limitations = [!complete ? "运行计数缺失或不完整，无法确认读取放大。" : !consistent ? "运行计数矛盾，无法确认读取放大。"
                : output == 0 ? "输出为 0，读取/输出倍数为 N/A，仍保留实测读取量和差值。" : "计数为同一算子的线程累计总量。",
                "不能由残差存在推断只有前导键参与 Seek，也不能证明新索引必然有效；需结合 Bitmap 等过滤与实际耗时核验。"],
            // Preserve the historical result ID while RULE006 continues to own configuration.
            LegacyResult = code == "RULE_007_NON_SARGABLE" ? new AnalysisResult { RuleId = code, Message = summary } : null
        });
    }

    private static bool IsScanOrSeek(string name) => name is "Index Seek" or "Clustered Index Seek"
        or "Index Scan" or "Clustered Index Scan" or "Table Scan" or "Columnstore Index Scan";

    private static RuleEvaluation Missing(string fields) => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "缺少可用证据：" + fields);

    private static DiagnosticEvidence Metric<T>(string name, PlanMetric<T> metric, PlanLocation location) where T : struct, IFormattable =>
        new(name, metric.IsAvailable ? metric.Value!.Value.ToString("G", CultureInfo.InvariantCulture) : null,
            metric.Source, location, metric.Unit, metric.State, metric.Kind, metric.Aggregation);

    private static DiagnosticEvidence Number<T>(string name, T value, string source, string unit, PlanLocation location) where T : IFormattable =>
        new(name, value.ToString("G", CultureInfo.InvariantCulture), source, location, unit, PlanMetricState.Available);

    private static void AddPredicates(List<DiagnosticEvidence> evidence, PlanOperatorFacts facts, PlanLocation location)
    {
        // Use the same complete evidence set for related rules, so protocol identity merges
        // their observations without dropping predicates or conflating other operators.
        foreach (string predicate in facts.Predicates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            evidence.Add(new("ResidualPredicate", predicate, "operator payload/Predicate|ProbeResidual|Residual/ScalarOperator/@ScalarString", location));
        foreach (string predicate in facts.SeekPredicates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            evidence.Add(new("SeekPredicate", predicate, "operator payload/SeekPredicates/ScalarOperator/@ScalarString", location));
    }
}
