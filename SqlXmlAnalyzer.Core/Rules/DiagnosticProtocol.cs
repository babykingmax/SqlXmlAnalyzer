using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Rules;

[JsonConverter(typeof(JsonStringEnumConverter<RuleRunStatus>))]
public enum RuleRunStatus { Hit, NoHit, Skipped, Failed }
[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticConfidence>))]
public enum DiagnosticConfidence { Unspecified, Low, Medium, High }

internal sealed class RuleSkippedException(string reasonCode, string reason) : Exception(reason)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record RuleConfigurationSnapshot(string RuleId, bool Enabled, string? SeverityOverride);

/// <summary>Plan values only; mutable XML/source lookup services are not exposed to rules.</summary>
public sealed class PlanAnalysisModel
{
    private static readonly ConditionalWeakTable<PlanDocument, PlanAnalysisModel> Models = new();
    internal static PlanAnalysisModel Get(PlanDocument? plan, DocumentEnvelope envelope) =>
        plan == null ? new(null, envelope) : Models.GetValue(plan, value => new(value, value.Envelope));
    internal PlanAnalysisModel(PlanDocument? plan, DocumentEnvelope envelope)
    {
        Envelope = envelope;
        // PlanDocument owns read-only value collections. Reuse them for node views;
        // copying every operator for every displayed node would be quadratic.
        Batches = plan?.Batches ?? Array.Empty<PlanBatch>();
        Statements = plan?.Statements ?? Array.Empty<PlanStatement>();
        Operators = plan?.Operators ?? Array.Empty<PlanOperator>();
    }
    public DocumentEnvelope Envelope { get; }
    public IReadOnlyList<PlanBatch> Batches { get; }
    public IReadOnlyList<PlanStatement> Statements { get; }
    public IReadOnlyList<PlanOperator> Operators { get; }
}

public sealed class PlanAnalysisContext
{
    internal PlanAnalysisContext(PlanDocument? plan, XElement source,
        IEnumerable<RuleConfigurationSnapshot> configuration, DocumentCapabilities? capabilities, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = plan?.Envelope ?? new DocumentEnvelope(
            ProtocolIdentity.Hash(source.Document?.ToString() ?? source.ToString()), null, null,
            AnalysisDocumentKind.ExecutionPlanXml, null, null, null, null);
        Model = PlanAnalysisModel.Get(plan, envelope);
        Configuration = new ReadOnlyDictionary<string, RuleConfigurationSnapshot>(configuration.ToDictionary(c => c.RuleId, StringComparer.Ordinal));
        // This context always retains an XML source. Match input recognition for
        // callers that do not supply a snapshot; explicit restrictions remain intact.
        Capabilities = capabilities ?? PlanCapabilityService.Get(source, cancellationToken);
        CancellationToken = cancellationToken;
    }
    public const string CurrentProtocolVersion = "1.0";
    public string ProtocolVersion => CurrentProtocolVersion;
    public PlanAnalysisModel Model { get; }
    public IReadOnlyDictionary<string, RuleConfigurationSnapshot> Configuration { get; }
    public DocumentCapabilities Capabilities { get; }
    public string? EngineVersion => Model.Envelope.EngineBuild;
    public string? SchemaVersion => Model.Envelope.SchemaVersion;
    [JsonIgnore] public CancellationToken CancellationToken { get; }
}

public sealed class RuleAnalysisContext
{
    internal RuleAnalysisContext(PlanAnalysisContext analysis, RuleMetadata metadata, PlanLocation location,
        PlanOperator? op, PlanOperatorFacts? facts, XElement legacyElement, XNamespace ns,
        PlanLocation documentLocation, PlanLocation? statementLocation)
    {
        Analysis = analysis; Metadata = metadata; Location = location; Operator = op; Facts = facts;
        LegacyElement = legacyElement; Namespace = ns;
        DocumentLocation = documentLocation; StatementLocation = statementLocation;
        SourceNodeId = op?.Location.Operator?.NodeId
            ?? (legacyElement.Name == ns + "RelOp" ? (string?)legacyElement.Attribute("NodeId") : null);
    }
    public PlanAnalysisContext Analysis { get; }
    public RuleMetadata Metadata { get; }
    public PlanLocation Location { get; }
    public PlanOperator? Operator { get; }
    public PlanOperatorFacts? Facts { get; }
    /// <summary>Local source identifier captured before evaluation; not a complete operator identity.</summary>
    public string? SourceNodeId { get; }
    public CancellationToken CancellationToken => Analysis.CancellationToken;
    public PlanLocation DocumentLocation { get; }
    public PlanLocation? StatementLocation { get; }
    public PlanLocation LocationFor(RuleScope scope) => scope switch
    {
        RuleScope.Plan => DocumentLocation,
        RuleScope.Statement => StatementLocation ?? Location,
        RuleScope.Operator => Operator?.Location ?? Location,
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };
    internal XElement LegacyElement { get; }
    internal XNamespace Namespace { get; }
}

public sealed record DiagnosticEvidence(string Name, string? Value, string Source, PlanLocation Location,
    string? Unit = null, PlanMetricState? State = null, PlanMetricKind? Kind = null, PlanMetricAggregation? Aggregation = null);

/// <summary>Authoring value. The engine defensively copies collections before publishing a diagnostic.</summary>
public sealed record DiagnosticProposal(string SemanticCode, string Title, string Summary, IssueSeverity Severity)
{
    public DiagnosticConfidence Confidence { get; init; } = DiagnosticConfidence.Unspecified;
    public RuleScope? Scope { get; init; }
    public IReadOnlyList<DiagnosticEvidence> Evidence { get; init; } = Array.Empty<DiagnosticEvidence>();
    public IReadOnlyList<string> Hypotheses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Applicability { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
    public string? LegacyExplanation { get; init; }
    internal AnalysisResult? LegacyResult { get; init; }
}

public sealed class RuleEvaluation
{
    private RuleEvaluation(RuleRunStatus status, string reasonCode, string reason, IEnumerable<DiagnosticProposal> diagnostics)
    {
        Status = status; ReasonCode = reasonCode; Reason = reason;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public RuleRunStatus Status { get; }
    public string ReasonCode { get; }
    public string Reason { get; }
    public IReadOnlyList<DiagnosticProposal> Diagnostics { get; }
    public static RuleEvaluation Hit(params DiagnosticProposal[] diagnostics) => new(RuleRunStatus.Hit, "RULE_HIT", "检测到符合条件的诊断。", diagnostics);
    public static RuleEvaluation NoHit(string reason = "已执行规则，未满足触发条件。") => new(RuleRunStatus.NoHit, "RULE_NO_HIT", reason, Array.Empty<DiagnosticProposal>());
    public static RuleEvaluation Skipped(string reasonCode, string reason) => new(RuleRunStatus.Skipped, reasonCode, reason, Array.Empty<DiagnosticProposal>());
}

public sealed record DiagnosticOrigin(string RuleId, string RuleVersion, RuleMetadata Metadata, string RunId,
    PlanLocation InvocationLocation);

public sealed class PlanDiagnostic
{
    internal PlanDiagnostic(string id, DiagnosticProposal proposal, PlanLocation location,
        IEnumerable<DiagnosticOrigin> origins, IEnumerable<SqlObjectReference> objects, string? sourceNodeId = null)
    {
        DiagnosticId = id; SemanticCode = proposal.SemanticCode; Title = proposal.Title; Summary = proposal.Summary;
        Severity = proposal.Severity; Confidence = proposal.Confidence; Location = location;
        Scope = proposal.Scope ?? origins.First().Metadata.Scope;
        Evidence = Array.AsReadOnly(proposal.Evidence.ToArray());
        Hypotheses = Freeze(proposal.Hypotheses); Recommendations = Freeze(proposal.Recommendations);
        Applicability = Freeze(proposal.Applicability); Limitations = Freeze(proposal.Limitations);
        Origins = Array.AsReadOnly(origins.OrderBy(o => o.RuleId, StringComparer.Ordinal).ThenBy(o => o.RunId, StringComparer.Ordinal).ToArray());
        Objects = Array.AsReadOnly(objects.ToArray());
        LegacyResult = proposal.LegacyResult;
        LegacyExplanation = proposal.LegacyExplanation;
        NodeId = location.Operator?.NodeId
            ?? (Scope == RuleScope.Operator ? sourceNodeId : null) ?? LegacyResult?.NodeId;
    }
    internal static IReadOnlyList<string> Freeze(IEnumerable<string> values) => Array.AsReadOnly(values.Distinct(StringComparer.Ordinal).ToArray());
    public string DiagnosticId { get; }
    public string SemanticCode { get; }
    // Preserve legacy result branches while Origins/RuleRun identify the rule that
    // executed and owns configuration. Native diagnostics use their primary origin.
    public string RuleId => LegacyResult?.RuleId ?? Origins[0].RuleId;
    public string RuleVersion => Origins[0].RuleVersion;
    public string Title { get; }
    public string Summary { get; }
    [JsonConverter(typeof(JsonStringEnumConverter<IssueSeverity>))] public IssueSeverity Severity { get; }
    public DiagnosticConfidence Confidence { get; }
    public PlanLocation Location { get; }
    /// <summary>Local NodeId for compatibility and fragments. Use Location for cross-statement identity.</summary>
    public string? NodeId { get; }
    [JsonConverter(typeof(JsonStringEnumConverter<RuleScope>))] public RuleScope Scope { get; }
    public IReadOnlyList<DiagnosticEvidence> Evidence { get; }
    public IReadOnlyList<string> Hypotheses { get; }
    public IReadOnlyList<string> Recommendations { get; }
    public IReadOnlyList<string> Applicability { get; }
    public IReadOnlyList<string> Limitations { get; }
    public IReadOnlyList<DiagnosticOrigin> Origins { get; }
    public IReadOnlyList<SqlObjectReference> Objects { get; }
    public string? LegacyExplanation { get; }
    internal AnalysisResult? LegacyResult { get; }
}

public sealed record RuleRun(string RunId, string RuleId, string RuleVersion, RuleMetadata Metadata,
    PlanLocation Location, RuleRunStatus Status, string ReasonCode, string Reason, double ElapsedMilliseconds,
    IReadOnlyList<string> DiagnosticIds, string? ExceptionType = null, UnexpectedErrorReport? FailureDiagnostic = null)
{
    /// <summary>Local operator identifier; absent for plan/statement invocations.</summary>
    public string? NodeId { get; init; }
}

public sealed class PlanDiagnosticReport
{
    internal PlanDiagnosticReport(PlanAnalysisContext context, IEnumerable<RuleRun> runs, IEnumerable<PlanDiagnostic> diagnostics)
    {
        Context = context; Runs = Array.AsReadOnly(runs.ToArray()); Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    [JsonIgnore] public PlanAnalysisContext Context { get; }
    public string ProtocolVersion => Context.ProtocolVersion;
    public string DocumentId => Context.Model.Envelope.DocumentId;
    public string? EngineVersion => Context.EngineVersion;
    public string? SchemaVersion => Context.SchemaVersion;
    [JsonConverter(typeof(JsonStringEnumConverter<DocumentCapabilities>))] public DocumentCapabilities Capabilities => Context.Capabilities;
    public IReadOnlyDictionary<string, RuleConfigurationSnapshot> Configuration => Context.Configuration;
    public IReadOnlyList<RuleRun> Runs { get; }
    public IReadOnlyList<PlanDiagnostic> Diagnostics { get; }
    public bool HasFailures => Runs.Any(r => r.Status == RuleRunStatus.Failed);
    public bool HasMissingEvidence => Runs.Any(r => r.Status == RuleRunStatus.Skipped && r.ReasonCode == "RULE_MISSING_EVIDENCE");
    public int HitCount => Runs.Count(r => r.Status == RuleRunStatus.Hit);
    public int NoHitCount => Runs.Count(r => r.Status == RuleRunStatus.NoHit);
    public int SkippedCount => Runs.Count(r => r.Status == RuleRunStatus.Skipped);
    public int FailedCount => Runs.Count(r => r.Status == RuleRunStatus.Failed);
    public List<AnalysisResult> ToLegacyResults()
    {
        var results = Diagnostics.Select(d => new AnalysisResult
        {
            RuleId = d.RuleId, Severity = d.Severity.ToString(), Title = d.Title,
            Message = d.LegacyResult?.Message ?? d.Summary,
            NodeId = d.NodeId ?? "",
            ResultScope = d.Scope,
            Location = d.Location, Objects = d.Objects, Metadata = d.Origins[0].Metadata, Diagnostic = d
        }).ToList();
        // Compatibility callers must never turn a failed invocation into an empty/healthy result.
        results.AddRange(Runs.Where(r => r.Status == RuleRunStatus.Failed).Select(r => new AnalysisResult
        {
            RuleId = r.RuleId, Severity = "Critical", Title = "诊断规则执行失败", Message = DiagnosticTextFormatter.FormatRun(r),
            NodeId = r.Location.Operator?.NodeId ?? r.NodeId ?? "", Location = r.Location, Metadata = r.Metadata,
            ResultScope = r.Metadata.Scope, Run = r
        }));
        return results;
    }
}

internal static class ProtocolIdentity
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    // Only explicitly equal semantic codes, locations and evidence merge. Titles/messages are not keys.
    public static string Diagnostic(string semanticCode, PlanLocation location, IEnumerable<DiagnosticEvidence> evidence) =>
        Hash(JsonSerializer.Serialize(new { semanticCode, Scope = location.DisplayScope,
            Evidence = evidence.Select(e => JsonSerializer.Serialize(e)).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray() }));
}
