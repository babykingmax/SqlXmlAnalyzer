using System.Collections.ObjectModel;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Privacy;

public enum PlanRedactionStatus { Ready, Blocked, Failed, Cancelled }

/// <summary>An immutable payload; blocked/failed results never expose a partially redacted document.</summary>
public sealed class PlanRedactionResult
{
    public const string CurrentPolicyVersion = "IMP05-1.0-SQL2022-1.571";
    public string PolicyVersion => CurrentPolicyVersion;
    public PlanRedactionStatus Status { get; }
    public bool CanExport => Status == PlanRedactionStatus.Ready;
    public string? Xml { get; }
    public IReadOnlyDictionary<string, int> MaskedCounts { get; }
    public IReadOnlyDictionary<string, int> UnsupportedCounts { get; }
    public UnexpectedErrorReport? Diagnostic { get; }
    public string Summary => $"脱敏状态：{Status}；策略：{PolicyVersion}；处理项：{MaskedCounts.Values.Sum()}；未覆盖项：{UnsupportedCounts.Values.Sum()}。" +
        (Status switch
        {
            PlanRedactionStatus.Ready => OutputPrivacy.RedactedNotice,
            PlanRedactionStatus.Blocked => "已停止导出；未覆盖类别需先实现并验证。",
            PlanRedactionStatus.Cancelled => "操作已取消，未生成脱敏副本。",
            _ => "处理失败，未生成脱敏副本。请检查诊断记录。"
        }) +
        (Diagnostic == null ? "" : " " + Diagnostic.Summary);

    internal PlanRedactionResult(PlanRedactionStatus status, string? xml, Dictionary<string, int> masked,
        Dictionary<string, int> unsupported, UnexpectedErrorReport? diagnostic = null)
    {
        Status = status;
        Xml = status == PlanRedactionStatus.Ready ? xml : null;
        MaskedCounts = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(masked));
        UnsupportedCounts = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(unsupported));
        Diagnostic = diagnostic;
    }
}
