using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Reporting;

public static class DiagnosticReportFactory
{
    public static string Scope(PlanLocation location) =>
        (location.Batch == null ? "Document" : $"B{location.Batch.BatchOrdinal}")
        + (location.Statement == null ? "" : $"/S{location.Statement.StatementOrdinal}")
        + (location.QueryPlan == null ? "" : $"/Q{location.QueryPlan.QueryPlanOrdinal}")
        + (location.Operator == null ? "" : $"/O{location.Operator.OperatorOrdinal}");

    public static bool Includes(PlanLocation location, PlanStatementKey? statement, PlanQueryPlanKey? queryPlan) =>
        statement == null || (location.Batch == null || location.Batch == statement.Batch)
            && (location.Statement == null || location.Statement == statement)
            && (queryPlan == null || location.QueryPlan == null || location.QueryPlan == queryPlan);

    public static DiagnosticReport Plan(XDocument document, PlanDocument model, PlanDiagnosticReport diagnostics,
        PlanStatementKey? statement = null, PlanQueryPlanKey? queryPlan = null, string? notices = null, string? context = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var budget = new ReportSizeBudget(token);
        if (diagnostics.DocumentId != model.Envelope.DocumentId || !ReferenceEquals(PlanIdentityAdapter.GetDocument(document), model))
            throw new InvalidDataException("报告与当前源文档版本不一致，请重新分析。");
        if (queryPlan != null && queryPlan.Statement != statement) throw new InvalidDataException("报告 QueryPlan 不属于所选语句。");
        if (statement != null && !model.Statements.Any(s => s.Key == statement)) throw new InvalidDataException("报告语句不属于当前文档。");
        if (queryPlan != null && !model.QueryPlans.Any(q => q.Key == queryPlan)) throw new InvalidDataException("报告 QueryPlan 不属于当前文档。");
        // Accessors reject mutation of the analyzed XML, including changes after selection.
        foreach (var entry in model.Statements) { token.ThrowIfCancellationRequested(); model.GetStatementSource(entry.Key); }
        var selected = model.Statements.Where(s => statement == null || s.Key == statement).ToArray();
        var operators = selected.SelectMany(s => s.QueryPlans).Where(q => queryPlan == null || q.Key == queryPlan).SelectMany(q => q.Operators).ToArray();
        var metadata = budget.Fields(Envelope(model.Envelope));
        metadata.Add(new("Capabilities", diagnostics.Capabilities.ToString()));
        metadata.Add(new("RuleProtocol", diagnostics.ProtocolVersion));
        metadata.Add(new("InputDiagnostics", budget.Json(model.Diagnostics), "DiagnosticText"));
        if (context != null) metadata.Add(new("InputContext", context, "Metadata"));
        metadata.Add(new("Configuration", budget.Json(diagnostics.Configuration), "Configuration"));
        if (!string.IsNullOrWhiteSpace(notices)) metadata.Add(new("DocumentRefactoringNotices", notices, "DiagnosticText"));
        var facts = budget.Fields();
        foreach (var s in selected) facts.Add(new(Scope(s.Location) + " SQL", s.Text ?? "N/A", "SqlText"));
        foreach (var op in operators)
        {
            string scope = Scope(op.Location);
            facts.Add(new(scope + " PhysicalOp", op.PhysicalOp ?? "N/A", "OperatorLabel"));
            facts.Add(new(scope + " LogicalOp", op.LogicalOp ?? "N/A", "OperatorLabel"));
            facts.Add(new(scope + " NodeId", op.Key.NodeId ?? "N/A", "SourceIdentity"));
            facts.Add(new(scope + " Source", budget.Json(op.Location.Source), "SourceLocation"));
            foreach (var target in op.Objects) facts.Add(new(scope + " Object", target.DisplayName, "ObjectIdentity"));
            if (op.ExportFacts is { } present)
            {
                using var json = budget.JsonDocument(present);
                AddFacts(facts, scope, json.RootElement);
            }
            else
            {
                // Same compact missing-data policy as PlanOperator.ExportFacts; do not fabricate zeros.
                facts.Add(new(scope + "/Metrics/State", "Missing"));
                facts.Add(new(scope + "/Metrics/Value", "N/A"));
            }
        }
        var issues = diagnostics.Diagnostics.Where(d => Includes(d.Location, statement, queryPlan)).Select((d, i) =>
        {
            var fields = budget.Fields([new("Title", d.Title, "DiagnosticText"), new("DiagnosticId", d.DiagnosticId, "SourceIdentity"),
                new("Details", DiagnosticTextFormatter.FormatDiagnostic(d), "DiagnosticText"), new("Source", budget.Json(d.Location.Source), "SourceLocation")]);
            fields.Add(new("Origins", budget.Json(d.Origins.Select(o => new { o.RuleId, o.RuleVersion }))));
            int evidenceIndex = 0;
            foreach (var evidence in d.Evidence)
            {
                string prefix = "Evidence" + ++evidenceIndex;
                fields.Add(new(prefix + "/Location", Scope(evidence.Location)));
                fields.Add(new(prefix + "/Name", evidence.Name, "DiagnosticText"));
                fields.Add(new(prefix + "/Value", evidence.Value ?? "N/A", "DiagnosticText"));
                fields.Add(new(prefix + "/State", evidence.State?.ToString() ?? "N/A"));
            }
            var item = new ReportItem($"D{i + 1}", d.RuleId, d.RuleVersion, Scope(d.Location), "Hit", d.Severity.ToString(), d.Confidence.ToString(), fields);
            budget.ItemHeader(item); return item;
        }).ToArray();
        var runs = diagnostics.Runs.Where(r => Includes(r.Location, statement, queryPlan) && r.ReasonCode != "RULE_OUTSIDE_SCOPE")
            .Select((r, i) => new ReportItem($"R{i + 1}", r.RuleId, r.RuleVersion, Scope(r.Location), r.Status.ToString(),
                r.Status == RuleRunStatus.Failed ? "Critical" : "N/A", "N/A",
                [new("ReasonCode", r.ReasonCode), new("Details", DiagnosticTextFormatter.FormatRun(r), "DiagnosticText")]))
            .Select(r => { budget.Item(r); return r; }).ToArray();
        var nodes = operators.Select(o => new ReportNode(Scope(o.Location), (o.PhysicalOp ?? "N/A") + " " + string.Join(", ", o.Objects.Select(x => x.DisplayName))))
            .Select(n => { budget.Node(n); return n; }).ToArray();
        var keys = operators.ToDictionary(o => o.Key, o => Scope(o.Location));
        var edges = operators.Where(o => o.Parent != null && keys.ContainsKey(o.Parent)).Select(o => new ReportEdge(keys[o.Parent!], keys[o.Key]))
            .Select(e => { budget.Edge(e); return e; }).ToArray();
        string xml = PlanReportXml.Select(document, model, statement, queryPlan, budget, token);
        Logger.Debug("IMP-22: captured immutable plan report selection.");
        return new("ExecutionPlan", statement == null ? "Document" : $"B{statement.Batch.BatchOrdinal}/S{statement.StatementOrdinal}" + (queryPlan == null ? "" : $"/Q{queryPlan.QueryPlanOrdinal}"),
            metadata, facts, issues, runs, nodes, edges, xml, token: token);
    }

    private static IEnumerable<ReportField> Envelope(DocumentEnvelope envelope) =>
    [new("DocumentId", envelope.DocumentId, "SourceIdentity"), new("SourceName", envelope.SourceName ?? "N/A", "Metadata"),
     new("SourceHash", envelope.SourceHash ?? "N/A", "SourceIdentity"), new("EngineBuild", envelope.EngineBuild ?? "N/A", "Metadata"),
     new("SourceSchema", envelope.SchemaVersion ?? "N/A", "Metadata"), new("CapturedAt", envelope.CapturedAt?.ToString("O") ?? "N/A", "Metadata")];

    private static void AddFacts(ICollection<ReportField> output, string path, JsonElement value, bool dictionary = false)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            int index = 0;
            foreach (var property in value.EnumerateObject())
            {
                // Dictionary keys can also be untrusted XML names. Keep them in classified values.
                string next = dictionary ? path + $"/Field{++index}" : path + "/" + property.Name;
                if (dictionary) output.Add(new(next + "/Name", property.Name, "XmlFieldName"));
                AddFacts(output, next, property.Value, property.Name is "SourceAttributes" or "RawFields");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0; foreach (var item in value.EnumerateArray()) AddFacts(output, path + $"[{++index}]", item);
        }
        else
        {
            string text = value.ValueKind == JsonValueKind.Null ? "N/A" : value.ToString();
            bool metricEnum = path.EndsWith("/State", StringComparison.Ordinal) && Enum.TryParse<PlanMetricState>(text, out var state) && Enum.IsDefined(state)
                || path.EndsWith("/Kind", StringComparison.Ordinal) && Enum.TryParse<PlanMetricKind>(text, out var kind) && Enum.IsDefined(kind)
                || path.EndsWith("/Aggregation", StringComparison.Ordinal) && Enum.TryParse<PlanMetricAggregation>(text, out var aggregation) && Enum.IsDefined(aggregation);
            bool metricUnit = path.EndsWith("/Unit", StringComparison.Ordinal)
                && text is "rows" or "rows/execution" or "bytes/row" or "bytes" or "executions" or "ms" or "pages" or "rebinds" or "rewinds" or "cost" or "optimizer-cost";
            output.Add(new(path, text, value.ValueKind == JsonValueKind.String && !metricEnum && !metricUnit ? "FactText" : ""));
        }
    }

    public static DiagnosticReport Deadlock(DeadlockInput input, DeadlockAnalysisOutput analysis, DocumentEnvelope? envelope = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var budget = new ReportSizeBudget(token);
        if (input.Document.Root is not { } root || root.Name.LocalName != "deadlock"
            || DeadlockIdentity.Hash(new ReportSizeBudget(token).Xml(root, indent: false)) != analysis.Graph.SourceFingerprint)
            throw new InvalidDataException("死锁诊断不属于当前事件版本，请重新分析。");
        string scope = "Event" + input.Index.ToString(CultureInfo.InvariantCulture);
        var metadata = budget.Fields(envelope == null ? null : Envelope(envelope));
        metadata.Add(new("EventSource", budget.Json(input.Location), "SourceLocation"));
        metadata.Add(new("Nature", "依赖关系快照，不是真实执行时间线；未证明根因。"));
        metadata.Add(new("Warnings", string.Join(Environment.NewLine, analysis.Warnings), "DiagnosticText"));
        var facts = budget.Fields();
        var nodes = budget.Collection<ReportNode>(budget.Node); var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var process in analysis.Processes)
        {
            string id = scope + "/P" + (keys.Count + 1); keys.Add(process.Id, id);
            nodes.Add(new(id, "SPID " + process.Spid));
            using var json = budget.JsonDocument(process);
            AddFacts(facts, id, json.RootElement);
        }
        int resourceIndex = 0;
        var resourceLocations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in analysis.Resources)
        {
            string location = scope + "/Resource" + ++resourceIndex;
            resourceLocations.Add(resource.Id, location);
            using var json = budget.JsonDocument(resource);
            AddFacts(facts, location, json.RootElement);
        }
        facts.Add(new("Cycle", analysis.Graph.CycleAnalysis.Summary, "DiagnosticText"));
        facts.Add(new("Victims", string.Join(", ", analysis.Graph.VictimProcessIds), "SourceIdentity"));
        var issues = analysis.Patterns.Select((p, i) =>
        {
            var fields = budget.Fields([new("Title", p.TypeName, "DiagnosticText"), new("Description", p.Description, "DiagnosticText"),
                new("Cause", p.LikelyCause, "DiagnosticText"), new("Recommendation", p.Recommendation, "DiagnosticText"),
                new("Evidence", DeadlockDiagnosticFormatter.FormatEvidence(p), "DiagnosticText")]);
            int index = 0;
            foreach (var evidence in p.Evidence)
                fields.Add(new($"Evidence{++index}/Location", (evidence.ProcessId == null ? scope : keys.GetValueOrDefault(evidence.ProcessId, scope))
                    + (evidence.ResourceId == null ? "" : " / " + resourceLocations.GetValueOrDefault(evidence.ResourceId, scope))));
            var item = new ReportItem($"D{i + 1}", p.RuleId, p.RuleVersion, scope, "Hit", p.Severity, p.Confidence.ToString(), fields);
            budget.ItemHeader(item); return item;
        }).ToArray();
        var edges = analysis.Graph.Edges.Where(e => keys.ContainsKey(e.FromProcessId) && keys.ContainsKey(e.ToProcessId))
            .Select(e => new ReportEdge(keys[e.FromProcessId], keys[e.ToProcessId]))
            .Select(e => { budget.Edge(e); return e; }).ToArray();
        return new("Deadlock", scope, metadata, facts, issues, [], nodes, edges, budget.Xml(input.Document), token: token);
    }
}
