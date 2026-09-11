using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer.Core.ViewModels;

public sealed record PlanWorkspaceFact(string Name, string Value);

/// <summary>Presentation projections leave diagnostic reports and the compatibility Issues collection intact.</summary>
public sealed partial class PlanWorkspaceViewModel
{
    private string _searchText = "";
    private string _severityFilter = "全部";
    public IReadOnlyList<string> SeverityFilters { get; } = Array.AsReadOnly(new[] { "全部", "Critical", "Warning", "Info" });
    public IReadOnlyList<PlanWorkspaceIssue> Findings { get; private set; } = Array.Empty<PlanWorkspaceIssue>();
    public IReadOnlyList<PlanWorkspaceIssue> CheckRuns { get; private set; } = Array.Empty<PlanWorkspaceIssue>();
    public IReadOnlyList<PlanWorkspaceIssue> FilteredFindings { get; private set; } = Array.Empty<PlanWorkspaceIssue>();
    public IReadOnlyList<PlanWorkspaceIssue> RelatedFindings { get; private set; } = Array.Empty<PlanWorkspaceIssue>();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == (value ?? "")) return;
            _searchText = value ?? "";
            OnPropertyChanged(nameof(SearchText));
            RefreshFindingFilter();
        }
    }
    public string SeverityFilter
    {
        get => _severityFilter;
        set
        {
            var filter = SeverityFilters.Contains(value) ? value : "全部";
            if (_severityFilter == filter) return;
            _severityFilter = filter;
            OnPropertyChanged(nameof(SeverityFilter));
            RefreshFindingFilter();
        }
    }

    public string FileSummary => Model == null ? "尚未打开执行计划" :
        $"{Path.GetFileName(Model.Envelope.SourceName) ?? "内存 XML"} · {Model.Statements.Count} 条语句"
        + (Selection == null ? "" : $" · 当前 {Selection.Operators.Count} 个算子 · {PlanDataSummary}");
    public string PlanDataSummary
    {
        get
        {
            var operators = Selection?.Choice.QueryPlan?.Operators;
            if (operators == null || operators.Count == 0) return "未采集算子";
            if (!operators.Any(op => op.Facts?.Threads.Count > 0))
                return operators.Any(op => op.Facts is { } facts && (facts.EstimatedRows.IsPresent || facts.SubtreeCost.IsPresent
                    || facts.EstimatedCpuCost.IsPresent || facts.EstimatedIoCost.IsPresent))
                    ? "仅估算信息（未采集运行计数）" : "未采集运行计数和估算指标";
            return operators.All(op => op.Facts?.Threads.Count > 0 && op.Facts.OutputRows.IsAvailable)
                ? "含实际运行信息" : "部分运行计数";
        }
    }
    public string AnalysisSummary => Report == null ? "尚未分析" :
        $"{Findings.Count} 个问题 · {CheckRuns.Count(i => i.Run!.Status == RuleRunStatus.Skipped)} 项未执行"
        + $" · {CheckRuns.Count(i => i.Run!.Status == RuleRunStatus.Failed)} 项检查失败"
        + (NeedsConfigurationReanalysis ? " · 配置已更改，需重新分析" : "");
    public string FindingsSummary => $"问题 {Findings.Count} · 当前显示 {FilteredFindings.Count}";
    public string CheckRunsSummary => $"检查状态 {CheckRuns.Count}";
    public bool HasFilteredFindings => FilteredFindings.Count > 0;
    public bool HasCheckRuns => CheckRuns.Count > 0;
    public bool HasFailedChecks => CheckRuns.Any(issue => issue.Run?.Status == RuleRunStatus.Failed);
    public bool HasRelatedFindings => RelatedFindings.Count > 0;
    public string FindingsEmptyMessage => Report == null ? "尚未完成分析，请打开执行计划并运行分析。"
        : Findings.Count > 0 ? "没有符合筛选条件的问题。可清空搜索或选择全部严重度。"
        : CheckRuns.Count > 0 ? "当前范围没有已发现的问题；部分检查未完成，请查看检查状态。"
        : "当前范围未发现问题；这不表示计划已通过所有性能检查。";
    public string RelatedFindingsSummary => SourceTarget?.Location.Operator == null ? "选择算子查看相关问题"
        : $"当前节点相关问题 {RelatedFindings.Count} · 全部问题保留在问题列表";

    public string DetailTitle { get; private set; } = "选择问题或算子";
    public string DetailSubtitle { get; private set; } = "点击问题列表或执行计划中的算子查看详情。";
    public IReadOnlyList<PlanWorkspaceFact> DetailFacts { get; private set; } = Array.Empty<PlanWorkspaceFact>();
    public string DetailReasons { get; private set; } = "尚未选择诊断。";
    public string DetailRecommendations { get; private set; } = "选择问题后查看建议。";
    public string DetailLimitations { get; private set; } = "";
    public bool HasDetailLimitations => DetailLimitations.Length > 0;
    public bool HasDetailFacts => DetailFacts.Count > 0;
    public string EvidenceSummary => Evidence.Count == 0 ? "当前选择没有诊断证据；可在来源详情中查看原始 XML。" : $"{Evidence.Count} 项原始诊断证据";
    public string SourceDetails => string.Join(Environment.NewLine + Environment.NewLine,
        new[] { Context, ConfigurationStatus, Status, SourceTarget?.Description, SelectedIssue?.Details }
            .Where(text => !string.IsNullOrWhiteSpace(text)));

    private void RefreshPresentationSelection()
    {
        Findings = Issues.Where(issue => issue.Diagnostic != null)
            .OrderByDescending(issue => SeverityRank(issue.Diagnostic!.Severity))
            .ThenByDescending(issue => (int)issue.Diagnostic!.Confidence)
            .ThenBy(issue => issue.Location.Batch?.BatchOrdinal ?? -1)
            .ThenBy(issue => issue.Location.Statement?.StatementOrdinal ?? -1)
            .ThenBy(issue => issue.Location.QueryPlan?.QueryPlanOrdinal ?? -1)
            .ThenBy(issue => issue.Location.Operator?.OperatorOrdinal ?? -1)
            .ThenBy(issue => issue.Diagnostic!.DiagnosticId, StringComparer.Ordinal).ToArray();
        CheckRuns = Issues.Where(issue => issue.Diagnostic == null && issue.Run?.Status is RuleRunStatus.Skipped or RuleRunStatus.Failed)
            .OrderBy(issue => issue.Run!.Status == RuleRunStatus.Failed ? 0 : 1)
            .ThenBy(issue => issue.Location.Batch?.BatchOrdinal ?? -1)
            .ThenBy(issue => issue.Location.Statement?.StatementOrdinal ?? -1)
            .ThenBy(issue => issue.Location.QueryPlan?.QueryPlanOrdinal ?? -1)
            .ThenBy(issue => issue.Location.Operator?.OperatorOrdinal ?? -1)
            .ThenBy(issue => issue.RuleId, StringComparer.Ordinal).ToArray();
        OnPropertyChanged(nameof(Findings));
        OnPropertyChanged(nameof(CheckRuns));
        OnPropertyChanged(nameof(HasCheckRuns));
        OnPropertyChanged(nameof(HasFailedChecks));
        OnPropertyChanged(nameof(CheckRunsSummary));
        RefreshFindingFilter();
        NotifyPresentationHeader();
    }

    private void RefreshFindingFilter()
    {
        var search = SearchText.Trim();
        FilteredFindings = Findings.Where(issue => (SeverityFilter == "全部" || issue.Severity == SeverityFilter)
            && (search.Length == 0 || ContainsSearch(issue, search))).ToArray();
        OnPropertyChanged(nameof(FilteredFindings));
        OnPropertyChanged(nameof(HasFilteredFindings));
        OnPropertyChanged(nameof(FindingsSummary));
        OnPropertyChanged(nameof(FindingsEmptyMessage));
    }

    private static bool ContainsSearch(PlanWorkspaceIssue issue, string search) =>
        new[] { issue.Title, issue.Summary, issue.RuleId, issue.Scope, issue.Location.Operator?.NodeId }
            .Any(text => text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
        || issue.Diagnostic!.Objects.Any(obj => obj.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));

    private void NotifyPresentationHeader()
    {
        OnPropertyChanged(nameof(FileSummary));
        OnPropertyChanged(nameof(PlanDataSummary));
        OnPropertyChanged(nameof(AnalysisSummary));
        OnPropertyChanged(nameof(SourceDetails));
    }

    private void RefreshPresentationDetails()
    {
        var selectedKey = SourceTarget?.Location.Operator;
        var selectedOperator = selectedKey == null ? null : Selection?.Choice.QueryPlan?.Operators.FirstOrDefault(op => op.Key == selectedKey);
        RelatedFindings = selectedKey == null ? Array.Empty<PlanWorkspaceIssue>() : Findings
            .Where(issue => issue.Location.Operator == selectedKey || issue.Evidence.Any(evidence => evidence.Location.Operator == selectedKey)).ToArray();
        var diagnostic = SelectedIssue?.Diagnostic;
        var run = SelectedIssue?.Run;
        var facts = new List<PlanWorkspaceFact>();
        DetailTitle = diagnostic?.Title ?? (run != null ? "检查未完成" : selectedOperator?.PhysicalOp ?? "选择问题或算子");
        DetailSubtitle = diagnostic != null ? $"{SelectedIssue!.Scope} · {diagnostic.Severity} · 置信度 {diagnostic.Confidence}"
            : run != null ? $"{SelectedIssue!.StatusLabel} · {run.RuleId} · {SelectedIssue.Scope}"
            : selectedOperator != null ? $"Node {selectedOperator.Key.NodeId ?? "未知"} · {RelatedFindings.Count} 个相关问题"
            : "点击问题列表或执行计划中的算子查看详情。";
        if (diagnostic != null) facts.Add(new("观察", diagnostic.Summary));
        if (run != null) facts.Add(new("检查结果", run.Reason));
        if (diagnostic == null && run == null && selectedOperator == null && Selection != null)
        {
            DetailTitle = "当前语句概览";
            DetailSubtitle = $"Statement {Selection.Choice.Statement.Key.StatementOrdinal} · {PlanDataSummary}";
            facts.Add(new("SQL 简要", BriefSql(Selection.Choice.Statement.Text)));
            facts.Add(new("算子数", Selection.Operators.Count.ToString()));
            facts.Add(new("已发现问题", Findings.Count.ToString()));
            facts.Add(new("检查完整性", CheckCompleteness()));
        }
        if (selectedOperator != null)
        {
            if (selectedOperator.Objects.Count > 0)
                facts.Add(new("对象", string.Join(Environment.NewLine, selectedOperator.Objects.Select(obj => obj.DisplayName))));
            if (selectedOperator.Facts is { } metrics)
            {
                facts.Add(new("估算输出行数（每次执行）", FormatMetric(metrics.EstimatedRows)));
                facts.Add(new("实际输出行数（总计）", FormatMetric(metrics.OutputRows)));
                facts.Add(new("实际读取行数（总计）", FormatMetric(metrics.RowsRead)));
                facts.Add(new("实际耗时（线程最大值）", FormatMetric(metrics.ElapsedTime)));
                facts.Add(new("估算算子成本", FormatMetric(metrics.OwnCost)));
            }
        }
        DetailFacts = facts.ToArray();
        DetailReasons = diagnostic != null ? JoinOr(diagnostic.Hypotheses, "未提供原因假设，请结合原始证据判断。")
            : run != null ? run.Reason : selectedOperator != null ? "当前节点未选中诊断；指标本身不代表已确认的性能问题。"
            : Selection != null ? "选择已发现的问题查看原因假设。" : "尚未选择诊断。";
        DetailRecommendations = diagnostic != null ? JoinOr(diagnostic.Recommendations, "未提供具体建议。可查看证据并补充采集。")
            : run?.Status == RuleRunStatus.Failed ? "查看来源详情中的失败原因，处理后重新分析。"
            : run != null ? "根据跳过原因补充所需证据或检查规则配置，再重新分析。"
            : selectedOperator != null ? "选择相关问题查看建议，或检查原始 SQL 和运行指标。" : "选择问题后查看建议。";
        DetailLimitations = diagnostic == null ? "" : string.Join(Environment.NewLine,
            diagnostic.Applicability.Select(text => "适用条件：" + text).Concat(diagnostic.Limitations.Select(text => "限制：" + text)));
        foreach (var property in new[] { nameof(RelatedFindings), nameof(HasRelatedFindings), nameof(RelatedFindingsSummary),
            nameof(DetailTitle), nameof(DetailSubtitle), nameof(DetailFacts), nameof(HasDetailFacts), nameof(DetailReasons),
            nameof(DetailRecommendations), nameof(DetailLimitations), nameof(HasDetailLimitations), nameof(EvidenceSummary), nameof(SourceDetails) })
            OnPropertyChanged(property);
    }

    private string CheckCompleteness()
    {
        if (Report == null || Selection == null) return "尚未分析";
        if (CheckRuns.Count > 0)
            return $"{CheckRuns.Count(issue => issue.Run!.Status == RuleRunStatus.Skipped)} 项未执行，"
                + $"{CheckRuns.Count(issue => issue.Run!.Status == RuleRunStatus.Failed)} 项检查失败；检查未完成";
        bool hasRuns = Report.Runs.Any(run => run.Location.DocumentId == Model!.Envelope.DocumentId
            && run.ReasonCode != "RULE_OUTSIDE_SCOPE"
            && Reporting.DiagnosticReportFactory.Includes(run.Location, Selection.Choice.Statement.Key, Selection.Choice.QueryPlan?.Key));
        return hasRuns ? "当前范围规则已执行；未发现问题不代表计划完全健康" : "尚无规则执行记录";
    }

    private static string BriefSql(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return "未采集 SQL 文本";
        var result = new StringBuilder(241);
        foreach (var character in sql)
        {
            if (char.IsWhiteSpace(character))
            {
                if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
            }
            else result.Append(character);
            if (result.Length > 240) return result.ToString(0, 240).TrimEnd() + "…";
        }
        return result.ToString().TrimEnd();
    }

    private static string FormatMetric<T>(PlanMetric<T> metric) where T : struct, IFormattable =>
        metric.IsAvailable ? metric.Display("G6") + (string.IsNullOrWhiteSpace(metric.Unit) ? "" : " " + metric.Unit)
            : metric.State switch
            {
                PlanMetricState.Missing => "未采集",
                PlanMetricState.Invalid => "数据无效",
                PlanMetricState.Incomplete => "数据不完整",
                PlanMetricState.Ambiguous => "口径不明确",
                _ => "未知"
            };
    private static string JoinOr(IReadOnlyList<string> lines, string fallback) => lines.Count > 0 ? string.Join(Environment.NewLine, lines) : fallback;
    private static int SeverityRank(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Critical => 3,
        IssueSeverity.Warning => 2,
        IssueSeverity.Info => 1,
        _ => 0
    };
}
