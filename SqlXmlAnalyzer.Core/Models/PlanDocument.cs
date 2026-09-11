using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Models;

public sealed record PlanBatchKey(string DocumentId, int BatchOrdinal)
{
    public override string ToString() => $"{DocumentId}/B{BatchOrdinal}";
}
public sealed record PlanStatementKey(PlanBatchKey Batch, int StatementOrdinal)
{
    public override string ToString() => $"{Batch}/S{StatementOrdinal}";
}
public sealed record PlanQueryPlanKey(PlanStatementKey Statement, int QueryPlanOrdinal)
{
    public override string ToString() => $"{Statement}/Q{QueryPlanOrdinal}";
}
public sealed record PlanOperatorKey(PlanQueryPlanKey QueryPlan, string? NodeId, int OperatorOrdinal)
{
    // The ordinal also disambiguates absent or repeated NodeId values within a QueryPlan.
    public override string ToString() => $"{QueryPlan}/N{(NodeId == null ? "~" : Uri.EscapeDataString(NodeId))}/O{OperatorOrdinal}";
}

public sealed record PlanLocation(string DocumentId, PlanBatchKey? Batch, PlanStatementKey? Statement,
    PlanQueryPlanKey? QueryPlan, PlanOperatorKey? Operator, SourceLocation Source)
{
    public string DisplayScope => Operator?.ToString() ?? QueryPlan?.ToString() ?? Statement?.ToString() ?? Batch?.ToString() ?? DocumentId;
}

/// <summary>Identifier parts are separate, case-preserving values. Null means unknown.</summary>
public sealed record SqlObjectIdentity(string? Server, string? Database, string? Schema, string? Object)
{
    public bool IsComplete => Server != null && Database != null && Schema != null && Object != null;
    public string DisplayName => string.Join(".", new[] { Server, Database, Schema, Object }
        .Select(part => part == null ? "?" : Quote(part)));
    public static string Quote(string part) => "[" + part.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public bool IsSameKnownObject(SqlObjectIdentity other) => IsComplete && other.IsComplete && this == other;

    public static string? DecodeIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')
            return value.Length == 2 ? null : value[1..^1].Replace("]]", "]", StringComparison.Ordinal);
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value.Length == 2 ? null : value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        return value;
    }
}

public sealed record SqlObjectReference(SqlObjectIdentity Identity, string? Alias, string? Index,
    string? TableReferenceId, PlanLocation Location, IReadOnlyDictionary<string, string> SourceAttributes)
{
    public string DisplayName => Identity.DisplayName + (Index == null ? "" : $" / Index {SqlObjectIdentity.Quote(Index)}") +
        (Alias == null ? "" : $" AS {SqlObjectIdentity.Quote(Alias)}");
}

public sealed record PlanOperator(PlanOperatorKey Key, PlanOperatorKey? Parent, string? PhysicalOp,
    string? LogicalOp, PlanLocation Location, IReadOnlyList<SqlObjectReference> Objects)
{
    [JsonIgnore] public PlanOperatorFacts? Facts { get; init; }
    // Avoid repeating large all-missing metric descriptions for empty operators in damaged input.
    [JsonPropertyName("Facts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlanOperatorFacts? ExportFacts => Facts?.HasData == true ? Facts : null;
}
public sealed record PlanQueryPlan(PlanQueryPlanKey Key, PlanLocation Location, IReadOnlyList<PlanOperator> Operators);
public sealed record PlanStatement(PlanStatementKey Key, PlanStatementKey? Parent, string Kind,
    string? StatementId, string? QueryHash, string? QueryPlanHash, string? Text,
    PlanLocation Location, IReadOnlyList<PlanQueryPlan> QueryPlans);
public sealed record PlanBatch(PlanBatchKey Key, PlanLocation Location, IReadOnlyList<PlanStatement> Statements);

public sealed class PlanDocument
{
    private readonly IReadOnlyDictionary<XElement, PlanLocation> _locations;
    private readonly IReadOnlyDictionary<PlanOperatorKey, XElement> _operatorSources;
    private readonly IReadOnlyDictionary<PlanQueryPlanKey, XElement> _queryPlanSources;
    private readonly IReadOnlyDictionary<PlanStatementKey, XElement> _statementSources;
    private readonly IReadOnlyDictionary<XElement, PlanOperator> _operators;
    private bool _sourceChanged;
    private readonly XDocument? _source;

    internal PlanDocument(DocumentEnvelope envelope, IReadOnlyList<PlanBatch> batches,
        IReadOnlyList<InputDiagnostic> diagnostics, Dictionary<XElement, PlanLocation> locations,
        Dictionary<PlanOperatorKey, XElement> operatorSources, Dictionary<PlanQueryPlanKey, XElement> queryPlanSources)
    {
        Envelope = envelope;
        Batches = batches;
        Diagnostics = diagnostics;
        _locations = new ReadOnlyDictionary<XElement, PlanLocation>(locations);
        _operatorSources = new ReadOnlyDictionary<PlanOperatorKey, XElement>(operatorSources);
        _queryPlanSources = new ReadOnlyDictionary<PlanQueryPlanKey, XElement>(queryPlanSources);
        _statementSources = new ReadOnlyDictionary<PlanStatementKey, XElement>(locations
            .Where(p => p.Value.Statement != null && p.Value.QueryPlan == null && PlanDocumentBuilder.IsStatement(p.Key.Name.LocalName))
            .ToDictionary(p => p.Value.Statement!, p => p.Key));
        Statements = Array.AsReadOnly(batches.SelectMany(b => b.Statements).ToArray());
        QueryPlans = Array.AsReadOnly(Statements.SelectMany(s => s.QueryPlans).ToArray());
        Operators = Array.AsReadOnly(QueryPlans.SelectMany(q => q.Operators).ToArray());
        _operators = new ReadOnlyDictionary<XElement, PlanOperator>(Operators.ToDictionary(o => operatorSources[o.Key]));
        XDocument? source = locations.Keys.FirstOrDefault()?.Document;
        _source = source;
        if (source != null)
        {
            var reference = new WeakReference<PlanDocument>(this);
            source.Changed += (_, _) => { if (reference.TryGetTarget(out var model)) model._sourceChanged = true; };
        }
    }

    public DocumentEnvelope Envelope { get; }
    public IReadOnlyList<PlanBatch> Batches { get; }
    public IReadOnlyList<InputDiagnostic> Diagnostics { get; }
    [JsonIgnore] public IReadOnlyList<PlanStatement> Statements { get; }
    [JsonIgnore] public IReadOnlyList<PlanQueryPlan> QueryPlans { get; }
    [JsonIgnore] public IReadOnlyList<PlanOperator> Operators { get; }

    // Compatibility methods expose the original source explicitly, never reconstructed XML.
    public bool IsCurrentSource(XDocument source) { EnsureCurrentSource(); return ReferenceEquals(_source, source); }
    public XElement? GetOperatorSource(PlanOperatorKey key) { EnsureCurrentSource(); return _operatorSources.GetValueOrDefault(key); }
    public XElement? GetQueryPlanSource(PlanQueryPlanKey key) { EnsureCurrentSource(); return _queryPlanSources.GetValueOrDefault(key); }
    public XElement? GetStatementSource(PlanStatementKey key) { EnsureCurrentSource(); return _statementSources.GetValueOrDefault(key); }
    public PlanOperator? FindOperator(XElement element) { EnsureCurrentSource(); return _operators.GetValueOrDefault(element); }
    public PlanLocation? FindLocation(XElement element)
    {
        EnsureCurrentSource();
        foreach (XElement ancestor in element.AncestorsAndSelf())
            if (_locations.TryGetValue(ancestor, out var location)) return location;
        return null;
    }

    private void EnsureCurrentSource()
    {
        if (_sourceChanged) throw new InvalidDataException("PLAN_SOURCE_CHANGED: 源 XML 已改变，请重新获取身份模型后定位。");
    }
}
