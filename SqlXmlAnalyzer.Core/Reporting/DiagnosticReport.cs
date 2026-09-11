using System.Collections.ObjectModel;
using SqlXmlAnalyzer.Core.Privacy;

namespace SqlXmlAnalyzer.Core.Reporting;

public sealed record ReportField(string Name, string Value, string Category = "");
public sealed record ReportNode(string Id, string Label);
public sealed record ReportEdge(string From, string To);

public sealed class ReportItem
{
    public ReportItem(string id, string ruleId, string ruleVersion, string location, string status,
        string severity, string confidence, IEnumerable<ReportField> fields)
    {
        Id = id; RuleId = ruleId; RuleVersion = ruleVersion; Location = location; Status = status;
        Severity = severity; Confidence = confidence;
        var budget = new ReportSizeBudget();
        budget.ItemHeader(this);
        Fields = budget.Freeze(fields, budget.Field);
    }
    public string Id { get; }
    public string RuleId { get; }
    public string RuleVersion { get; }
    public string Location { get; }
    public string Status { get; }
    public string Severity { get; }
    public string Confidence { get; }
    public IReadOnlyList<ReportField> Fields { get; }
}

/// <summary>A detached, immutable selection snapshot. Renderers must not query a live view or analyze XML again.</summary>
public sealed class DiagnosticReport
{
    internal DiagnosticReport(string kind, string scope, IEnumerable<ReportField> metadata, IEnumerable<ReportField> facts,
        IEnumerable<ReportItem> issues, IEnumerable<ReportItem> runs, IEnumerable<ReportNode> nodes,
        IEnumerable<ReportEdge> edges, string xml, bool redacted = false, DateTimeOffset? createdAt = null,
        CancellationToken token = default)
    {
        var budget = new ReportSizeBudget(token);
        budget.Value(kind); budget.Value(scope); budget.Value(xml);
        Kind = kind; Scope = scope; Metadata = budget.Freeze(metadata, budget.Field); Facts = budget.Freeze(facts, budget.Field);
        Issues = budget.Freeze(issues, budget.Item); Runs = budget.Freeze(runs, budget.Item);
        Nodes = budget.Freeze(nodes, budget.Node); Edges = budget.Freeze(edges, budget.Edge); SourceXml = xml;
        IsRedacted = redacted; CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }
    public const string CurrentVersion = "IMP22-1.0";
    public string SchemaVersion => CurrentVersion;
    public string ProductVersion => ProductInfo.Version;
    public string Kind { get; }
    public string Scope { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsRedacted { get; }
    public string PrivacyNotice => IsRedacted
        ? "已脱敏诊断报告：自由文本已整体替换；保留结构定位、指标状态、规则及问题数量。XML 仅供结构核对，不可执行 SQL。"
        : OutputPrivacy.RawNotice;
    public string Capabilities => "离线静态关系图；不是实际执行时间线。图超过 80 个节点时展示前 80 个，完整节点/边仍列入正文与 JSON。HTML/PDF/Word/JSON 包含同一事实、诊断、运行记录及 XML；SVG 仅包含静态图，sqlplan 仅包含选择范围 XML；无交互回放。";
    public int IssueCount => Issues.Count;
    public int FactCount => Facts.Count;
    public int HitCount => Runs.Count(r => r.Status == "Hit");
    public int NoHitCount => Runs.Count(r => r.Status == "NoHit");
    public int SkippedCount => Runs.Count(r => r.Status == "Skipped");
    public int FailedCount => Runs.Count(r => r.Status == "Failed");
    public IReadOnlyList<ReportField> Metadata { get; }
    public IReadOnlyList<ReportField> Facts { get; }
    public IReadOnlyList<ReportItem> Issues { get; }
    public IReadOnlyList<ReportItem> Runs { get; }
    public IReadOnlyList<ReportNode> Nodes { get; }
    public IReadOnlyList<ReportEdge> Edges { get; }
    public string SourceXml { get; }
}
