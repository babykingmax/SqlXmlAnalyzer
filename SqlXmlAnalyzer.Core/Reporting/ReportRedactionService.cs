using System.Collections.ObjectModel;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Privacy;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Reporting;

public sealed record RedactionSample(string Category, string Before, string After);
public sealed class ReportRedactionPreview
{
    internal ReportRedactionPreview(DiagnosticReport? report, Dictionary<string, int> counts, Dictionary<string, int> unsupported,
        IEnumerable<RedactionSample> samples, string status)
    {
        Report = report; Counts = new ReadOnlyDictionary<string, int>(counts); Unsupported = new ReadOnlyDictionary<string, int>(unsupported);
        Samples = Array.AsReadOnly(samples.ToArray()); Status = status;
    }
    public string PolicyVersion => "IMP22-1.0 + " + PlanRedactionResult.CurrentPolicyVersion;
    public DiagnosticReport? Report { get; }
    public bool CanExport => Report != null;
    public IReadOnlyDictionary<string, int> Counts { get; }
    public IReadOnlyDictionary<string, int> Unsupported { get; }
    public IReadOnlyList<RedactionSample> Samples { get; }
    public string Status { get; }
    public string Summary => $"策略：{PolicyVersion}；已处理 {Counts.Values.Sum()} 项；未覆盖 {Unsupported.Values.Sum()} 项。{Status}";
}

public sealed class ReportRedactionService(IUnexpectedErrorReporter? unexpectedErrors = null)
{
    public ReportRedactionPreview Preview(DiagnosticReport original, CancellationToken token = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var unsupported = new Dictionary<string, int>(StringComparer.Ordinal);
        var samples = new List<RedactionSample>();
        var values = new Dictionary<(string, string), string>();
        void Count(string category) => counts[category] = counts.GetValueOrDefault(category) + 1;
        string Mask(string value, string category)
        {
            token.ThrowIfCancellationRequested();
            if (value.Length == 0) return value;
            Count(category);
            if (!values.TryGetValue((category, value), out string? replacement))
                values[(category, value)] = replacement = $"[Masked_{category}_{values.Count + 1}]";
            if (samples.Count < 30 && !samples.Any(s => s.Category == category && s.Before == value))
                samples.Add(new(category, value.Length > 200 ? value[..200] + "…" : value, replacement));
            return replacement;
        }
        ReportField Field(ReportField value) => value.Category.Length == 0 ? value : value with { Value = Mask(value.Value, value.Category) };
        ReportItem Item(ReportItem value) => new(value.Id, value.RuleId, value.RuleVersion, value.Location, value.Status,
            value.Severity, value.Confidence, value.Fields.Select(Field));
        try
        {
            token.ThrowIfCancellationRequested();
            Logger.Debug("IMP-22: preparing redaction preview.");
            var source = SafeXmlHelper.ParseSafe(original.SourceXml, new DocumentReadOptions(), token);
            string xml;
            if (original.Kind == "ExecutionPlan")
            {
                var plan = new PlanRedactionService(unexpectedErrors).Redact(source, token);
                foreach (var pair in plan.MaskedCounts) counts["Xml." + pair.Key] = pair.Value;
                foreach (var pair in plan.UnsupportedCounts) unsupported["Xml." + pair.Key] = pair.Value;
                if (!plan.CanExport) return new(null, counts, unsupported, samples, plan.Summary);
                xml = plan.Xml!;
            }
            else
            {
                // This is a structural illustration, not a reusable .xdl: remove every attribute
                // (including names), text, comment and PI. Unknown element names block publication.
                var known = new HashSet<string>("deadlock victim-list victimProcess process-list process executionStack frame inputbuf resource-list keylock pagelock ridlock objectlock exchangeEvent owner-list owner waiter-list waiter applicationlock metadatalock threadpool xactlock communicationBuffer mutex session transactionlock".Split(' '), StringComparer.Ordinal);
                foreach (var element in source.Descendants())
                {
                    token.ThrowIfCancellationRequested();
                    if (element.Name.NamespaceName.Length != 0 || !known.Contains(element.Name.LocalName))
                        unsupported["Xml.UnknownElementOrNamespace"] = unsupported.GetValueOrDefault("Xml.UnknownElementOrNamespace") + 1;
                    foreach (var attribute in element.Attributes().ToArray()) { Count("Xml.RemovedAttribute"); attribute.Remove(); }
                }
                foreach (var node in source.DescendantNodes().Where(n => n is not XElement).ToArray()) { Count("Xml.RemovedContent"); node.Remove(); }
                if (unsupported.Count > 0)
                {
                    Logger.Warning("IMP-22: redaction blocked by unsupported XML fields.");
                    return new(null, counts, unsupported, samples, "未覆盖的 XML 元素已阻止脱敏导出。原始数据未改变。");
                }
                xml = source.ToString();
            }
            var result = new DiagnosticReport(original.Kind, original.Scope, original.Metadata.Select(Field), original.Facts.Select(Field),
                original.Issues.Select(Item), original.Runs.Select(Item), original.Nodes.Select(n => n with { Label = Mask(n.Label, "GraphLabel") }),
                original.Edges, xml, true, original.CreatedAt, token);
            token.ThrowIfCancellationRequested();
            Logger.Debug("IMP-22: redaction preview is ready.");
            return new(result, counts, unsupported, samples, "可导出当前脱敏快照。自由文本整体替换；死锁 XML 删除所有属性/文本，仅保留已支持元素结构。");
        }
        catch (Exception exception)
        {
            return new(null, counts, unsupported, samples, ExceptionPolicy.Describe(exception, "IMP22.ReportRedaction.Preview", unexpectedErrors));
        }
    }
}
