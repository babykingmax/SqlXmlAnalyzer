using System.Text;

namespace SqlXmlAnalyzer.Core.Rules;

/// <summary>The same IDs, evidence and execution states are used by GUI, CLI and text/HTML reports.</summary>
public static class DiagnosticTextFormatter
{
    public static string Summary(PlanDiagnosticReport report) =>
        $"规则执行：Hit {report.HitCount} / NoHit {report.NoHitCount} / Skipped {report.SkippedCount} / Failed {report.FailedCount}。";

    public static string FormatRun(RuleRun run) =>
        $"[{run.Status}] {run.RuleId} v{run.RuleVersion} [{run.Location.DisplayScope}]：{run.ReasonCode} — {run.Reason}"
        + (run.Location.Operator == null && run.NodeId != null ? $"；局部 NodeId：{run.NodeId}" : "")
        + (run.FailureDiagnostic == null ? "" : $"；{run.FailureDiagnostic.Summary}");

    public static string FormatExecutionStatus(PlanDiagnosticReport report) => string.Join(Environment.NewLine,
        new[] { Summary(report) }.Concat(report.Runs
            .Where(r => (r.Status is RuleRunStatus.Skipped or RuleRunStatus.Failed) && r.ReasonCode != "RULE_OUTSIDE_SCOPE")
            .Select(FormatRun)));

    public static string FormatDiagnostic(PlanDiagnostic diagnostic)
    {
        var text = new StringBuilder();
        text.AppendLine($"[{diagnostic.Severity}] {diagnostic.Title}");
        text.AppendLine($"诊断 ID：{diagnostic.DiagnosticId}");
        text.AppendLine($"来源：{diagnostic.Location.DisplayScope}；置信度：{diagnostic.Confidence}");
        if (diagnostic.Location.Operator == null && diagnostic.NodeId != null)
            text.AppendLine($"局部 NodeId：{diagnostic.NodeId}");
        text.AppendLine("规则：" + string.Join(", ", diagnostic.Origins.Select(o => $"{o.RuleId} v{o.RuleVersion}")));
        if (diagnostic.Origins.All(o => o.RuleId != diagnostic.RuleId)) text.AppendLine("结果规则：" + diagnostic.RuleId);
        text.AppendLine(diagnostic.Summary);
        foreach (var evidence in diagnostic.Evidence)
            text.AppendLine($"证据 {evidence.Name}：{evidence.Value ?? "N/A"}"
                + (evidence.Unit == null ? "" : $" {evidence.Unit}")
                + (evidence.State == null ? "" : $"；{evidence.State}/{evidence.Kind}/{evidence.Aggregation}")
                + $"；{evidence.Source}；{evidence.Location.DisplayScope}");
        foreach (string hypothesis in diagnostic.Hypotheses) text.AppendLine("假设：" + hypothesis);
        foreach (string recommendation in diagnostic.Recommendations) text.AppendLine("建议：" + recommendation);
        foreach (string condition in diagnostic.Applicability) text.AppendLine("适用条件：" + condition);
        foreach (string limitation in diagnostic.Limitations) text.AppendLine("限制：" + limitation);
        if (diagnostic.LegacyExplanation != null) text.AppendLine("兼容说明（包含待核验推断与建议）：" + diagnostic.LegacyExplanation);
        return text.ToString().TrimEnd();
    }

    public static string Format(PlanDiagnosticReport report)
    {
        var text = new StringBuilder();
        text.AppendLine($"执行计划诊断协议 v{report.ProtocolVersion}");
        text.AppendLine(Summary(report));
        if (report.HasFailures) text.AppendLine("存在规则执行失败，诊断不完整，不能据此判断计划健康。");
        else if (report.HasMissingEvidence) text.AppendLine("部分检查缺少证据而跳过；未命中不代表这些检查已通过。");
        else if (report.Diagnostics.Count == 0) text.AppendLine("已执行的规则未命中；此结果不构成计划完全健康的证明。");
        foreach (var diagnostic in report.Diagnostics) { text.AppendLine(); text.AppendLine(FormatDiagnostic(diagnostic)); }
        text.AppendLine();
        foreach (var run in report.Runs.Where(r => r.Status is RuleRunStatus.Skipped or RuleRunStatus.Failed)) text.AppendLine(FormatRun(run));
        return text.ToString().TrimEnd();
    }
}
