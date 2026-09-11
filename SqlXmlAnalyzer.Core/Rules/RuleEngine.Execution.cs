using System.Diagnostics;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Rules;

public partial class RuleEngine
{
    public List<AnalysisResult> AnalyzePlan(XDocument document, XNamespace ns, PlanDocument? identities = null) =>
        document.Root == null ? new() : AnalyzePlanDetailed(document, ns, identities).ToLegacyResults();

    public List<AnalysisResult> AnalyzeNode(XElement relOp, XNamespace ns) => AnalyzeNodeDetailed(relOp, ns).ToLegacyResults();

    public PlanDiagnosticReport AnalyzePlanDetailed(XDocument document, XNamespace ns, PlanDocument? identities = null,
        DocumentCapabilities? capabilities = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ns);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.Root == null) throw new InvalidDataException("RULE_DOCUMENT_EMPTY: 诊断需要 XML 根节点。");
        identities ??= PlanIdentityAdapter.GetDocument(document, cancellationToken);
        if (identities != null && identities.FindLocation(document.Root) == null)
            throw new InvalidDataException("RULE_MODEL_SOURCE_MISMATCH: 身份模型不属于当前输入文档。");
        var analysis = CreateAnalysisContext(identities, document.Root, capabilities, cancellationToken);
        var runs = new List<RuleRun>();
        var diagnostics = new Dictionary<string, PlanDiagnostic>(StringComparer.Ordinal);
        // Legacy XML is isolated from the user's source. Synthetic contexts are prepared
        // once, and each invocation is guarded against changing this shared working copy.
        var working = new XDocument(document);
        var sources = working.Descendants().Zip(document.Descendants()).ToDictionary(p => p.First, p => p.Second);
        var relOps = working.Descendants(ns + "RelOp").Where(e => identities == null || identities.FindOperator(sources[e]) != null).ToList();
        var statements = working.Descendants().Where(e => e.Name.Namespace == ns
                && PlanDocumentBuilder.IsStatement(e.Name.LocalName) && e.Parent?.Name == ns + "Statements")
            .Where(e => !e.Ancestors().Any(a => a.Name.Namespace != ns || a.Name.LocalName == "InternalInfo")).ToList();
        XElement planElement = relOps.FirstOrDefault() ?? Temporary(working.Root!, ns);
        var statementElements = statements.Select(statement => statement.Descendants(ns + "RelOp").FirstOrDefault(op =>
                !op.Ancestors().TakeWhile(a => a != statement).Any(a => a.Name == ns + "Statements" || a.Name == ns + "InternalInfo"))
            ?? Temporary(statement, ns)).ToList();
        var queryPlanElements = working.Descendants(ns + "QueryPlan")
            .Where(query => identities == null ? QueryPlanXml.IsInScope(query, ns)
                : identities.FindLocation(sources[query])?.QueryPlan != null)
            .ToList().Select(query => query.Elements(ns + "RelOp").FirstOrDefault() ?? Temporary(query, ns)).ToList();
        // The isolated XML copy represents the same captured document. Register after
        // preparing synthetic contexts so change notifications cannot replace this identity.
        PlanIdentityAdapter.SetEnvelope(working, analysis.Model.Envelope);
        foreach (IPlanAnalyzerRule rule in _rules.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            RuleMetadata metadata = _metadata[rule];
            IReadOnlyList<XElement> contexts = metadata.Scope switch
            {
                RuleScope.Plan => new[] { planElement }, RuleScope.Statement => statementElements,
                RuleScope.QueryPlan => queryPlanElements,
                RuleScope.Operator => relOps, _ => throw new InvalidOperationException("RULE_SCOPE_INVALID")
            };
            if (contexts.Count == 0)
            {
                var context = CreateInvocation(analysis, metadata, document.Root, planElement, ns, identities);
                Execute(rule, context, runs, diagnostics, RuleEvaluation.Skipped("RULE_NO_CONTEXT", "计划中不存在该规则所需的语句或算子。"));
            }
            foreach (XElement element in contexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                XElement source = element.AncestorsAndSelf().Select(e => sources.GetValueOrDefault(e)).First(e => e != null)!;
                Execute(rule, CreateInvocation(analysis, metadata, source, element, ns, identities), runs, diagnostics);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        Logger.Debug($"IMP-12: 规则诊断完成；调用数 {runs.Count}，诊断数 {diagnostics.Count}。");
        return new(analysis, runs, diagnostics.Values);
    }

    public PlanDiagnosticReport AnalyzeNodeDetailed(XElement relOp, XNamespace ns, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relOp);
        ArgumentNullException.ThrowIfNull(ns);
        cancellationToken.ThrowIfCancellationRequested();
        if (relOp.Name != ns + "RelOp") throw new InvalidDataException("RULE_EXPECTED_RELOP: 算子诊断需要 RelOp。");
        var identities = PlanIdentityAdapter.GetDocument(relOp.Document, cancellationToken);
        var analysis = CreateAnalysisContext(identities, relOp, null, cancellationToken);
        var runs = new List<RuleRun>();
        var diagnostics = new Dictionary<string, PlanDiagnostic>(StringComparer.Ordinal);
        XElement? firstPlanOp = relOp.Document?.Descendants(ns + "RelOp").FirstOrDefault(e => identities == null || identities.FindOperator(e) != null);
        XElement? statement = relOp.Ancestors().FirstOrDefault(e => e.Name.Namespace == ns
            && PlanDocumentBuilder.IsStatement(e.Name.LocalName) && e.Parent?.Name == ns + "Statements");
        XElement? firstStatementOp = statement?.Descendants(ns + "RelOp").FirstOrDefault(e =>
            !e.Ancestors().TakeWhile(a => a != statement).Any(a => a.Name == ns + "Statements" || a.Name == ns + "InternalInfo"));
        XElement? firstQueryPlanOp = QueryPlanXml.Find(relOp, ns)?.Elements(ns + "RelOp").FirstOrDefault();
        foreach (IPlanAnalyzerRule rule in _rules.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            RuleMetadata metadata = _metadata[rule];
            bool applicable = metadata.Scope switch
            {
                RuleScope.Operator => true,
                RuleScope.Plan => ReferenceEquals(relOp, firstPlanOp),
                RuleScope.QueryPlan => ReferenceEquals(relOp, firstQueryPlanOp),
                RuleScope.Statement => ReferenceEquals(relOp, firstStatementOp), _ => false
            };
            Execute(rule, CreateInvocation(analysis, metadata, relOp, relOp, ns, identities), runs, diagnostics,
                applicable ? null : RuleEvaluation.Skipped("RULE_OUTSIDE_SCOPE", "该计划或语句规则由对应范围的首个算子执行。"));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(analysis, runs, diagnostics.Values);
    }

    private PlanAnalysisContext CreateAnalysisContext(PlanDocument? identities, XElement source,
        DocumentCapabilities? capabilities, CancellationToken cancellationToken) => new(identities, source,
        _rules.Select(rule =>
        {
            var setting = FindConfiguration(rule);
            return new RuleConfigurationSnapshot(_metadata[rule].RuleId, setting?.Enabled ?? true, setting?.SeverityOverride);
        }), capabilities, cancellationToken);

    private static RuleAnalysisContext CreateInvocation(PlanAnalysisContext analysis, RuleMetadata metadata,
        XElement source, XElement legacy, XNamespace ns, PlanDocument? identities)
    {
        var locator = new DocumentSource(analysis.CancellationToken);
        XElement root = source.Document?.Root ?? source;
        PlanLocation Fallback(XElement element) => new(analysis.Model.Envelope.DocumentId, null, null, null, null, locator.Location(element));
        PlanLocation documentLocation = identities?.FindLocation(root) ?? Fallback(root);
        XElement? statement = source.AncestorsAndSelf().FirstOrDefault(e => e.Name.Namespace == ns
            && PlanDocumentBuilder.IsStatement(e.Name.LocalName) && e.Parent?.Name == ns + "Statements");
        PlanLocation? statementLocation = statement == null ? null : identities?.FindLocation(statement) ?? Fallback(statement);
        XElement? queryPlan = QueryPlanXml.Find(source, ns);
        PlanLocation? queryPlanLocation = queryPlan == null ? null : identities?.FindLocation(queryPlan) ?? Fallback(queryPlan);
        PlanOperator? op = identities?.FindOperator(source);
        PlanLocation location = metadata.Scope switch
        {
            RuleScope.Plan => documentLocation,
            RuleScope.QueryPlan => queryPlanLocation ?? documentLocation,
            RuleScope.Statement => statementLocation ?? documentLocation,
            _ => op?.Location ?? identities?.FindLocation(source) ?? Fallback(source)
        };
        var facts = op?.Facts ?? (source.Name == ns + "RelOp" ? PlanOperatorFactsService.Get(source, ns, analysis.CancellationToken) : null);
        return new(analysis, metadata, location, op, facts, legacy, ns, documentLocation, statementLocation, queryPlanLocation);
    }

    private void Execute(IPlanAnalyzerRule rule, RuleAnalysisContext context, List<RuleRun> runs,
        Dictionary<string, PlanDiagnostic> diagnostics, RuleEvaluation? skipped = null)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        RuleMetadata metadata = context.Metadata;
        string? nodeId = metadata.Scope == RuleScope.Operator ? context.SourceNodeId : null;
        string runId = ProtocolIdentity.Hash(metadata.RuleId + "\n" + metadata.Version + "\n" + context.Location.DisplayScope
            + "\n" + context.Location.Source.XmlPath);
        var clock = Stopwatch.StartNew();
        Logger.Debug($"IMP-12: 规则开始 {metadata.RuleId} v{metadata.Version}。");
        try
        {
            var setting = context.Analysis.Configuration[metadata.RuleId];
            RuleEvaluation evaluation;
            if (!setting.Enabled) evaluation = RuleEvaluation.Skipped("RULE_DISABLED", "规则已由配置禁用。");
            else if (skipped != null) evaluation = skipped;
            else
            {
                XObject guarded = (XObject?)context.LegacyElement.Document ?? context.LegacyElement;
                void RejectMutation(object? sender, XObjectChangeEventArgs args) =>
                    throw new InvalidOperationException("RULE_SOURCE_MUTATION: 规则不得修改分析上下文中的 XML。");
                guarded.Changing += RejectMutation;
                try { evaluation = rule.Evaluate(context); }
                finally { guarded.Changing -= RejectMutation; }
            }
            context.CancellationToken.ThrowIfCancellationRequested();
            if (evaluation == null || (evaluation.Status == RuleRunStatus.Hit) != (evaluation.Diagnostics.Count > 0))
                throw new InvalidOperationException("RULE_OUTCOME_INVALID: Hit 必须包含诊断，其他状态不得包含诊断。");
            var published = new List<PlanDiagnostic>();
            foreach (DiagnosticProposal proposed in evaluation.Diagnostics)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (proposed == null || string.IsNullOrWhiteSpace(proposed.SemanticCode)
                    || !Enum.IsDefined(proposed.Severity) || !Enum.IsDefined(proposed.Confidence)
                    || (proposed.Scope.HasValue && !Enum.IsDefined(proposed.Scope.Value))
                    || proposed.Evidence == null || proposed.Evidence.Count == 0
                    || proposed.Evidence.Any(e => e == null || string.IsNullOrWhiteSpace(e.Name)
                        || string.IsNullOrWhiteSpace(e.Source) || e.Location == null
                        || e.Location.DocumentId != context.Analysis.Model.Envelope.DocumentId))
                    throw new InvalidOperationException("RULE_EVIDENCE_INVALID: 诊断必须包含本输入的可定位证据及语义编号。");
                var proposal = proposed with
                {
                    Severity = setting.SeverityOverride == null ? proposed.Severity : LegacyDiagnosticAdapter.ParseSeverity(setting.SeverityOverride),
                    LegacyResult = proposed.LegacyResult == null ? null : new AnalysisResult
                        { RuleId = proposed.LegacyResult.RuleId, Message = proposed.LegacyResult.Message, NodeId = proposed.LegacyResult.NodeId }
                };
                PlanLocation location = context.LocationFor(proposal.Scope ?? metadata.Scope);
                if (((proposal.Scope ?? metadata.Scope) == RuleScope.Operator && context.Facts == null)
                    || ((proposal.Scope ?? metadata.Scope) == RuleScope.QueryPlan && context.QueryPlanLocation == null)
                    || ((proposal.Scope ?? metadata.Scope) == RuleScope.Statement && context.StatementLocation == null))
                    throw new InvalidOperationException("RULE_LOCATION_UNAVAILABLE: 诊断要求的语句或算子位置不存在。");
                string id = ProtocolIdentity.Diagnostic(proposal.SemanticCode, location, proposal.Evidence);
                published.Add(new PlanDiagnostic(id, proposal, location,
                    new[] { new DiagnosticOrigin(metadata.RuleId, metadata.Version, metadata, runId, context.Location) },
                    location.Operator != null ? context.Operator?.Objects ?? Array.Empty<SqlObjectReference>() : Array.Empty<SqlObjectReference>(),
                    context.SourceNodeId));
            }
            // Publish atomically only after every proposal passes validation.
            foreach (var diagnostic in published)
                diagnostics[diagnostic.DiagnosticId] = diagnostics.TryGetValue(diagnostic.DiagnosticId, out var prior) ? Merge(prior, diagnostic) : diagnostic;
            runs.Add(new(runId, metadata.RuleId, metadata.Version, metadata, context.Location, evaluation.Status,
                evaluation.ReasonCode, evaluation.Reason, clock.Elapsed.TotalMilliseconds,
                Array.AsReadOnly(published.Select(d => d.DiagnosticId).Distinct(StringComparer.Ordinal).ToArray())) { NodeId = nodeId });
            if (evaluation.Status == RuleRunStatus.Skipped) Logger.Warning($"IMP-12: 规则跳过 {metadata.RuleId}，原因 {evaluation.ReasonCode}。");
            else Logger.Debug($"IMP-12: 规则完成 {metadata.RuleId}，状态 {evaluation.Status}。");
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { throw; }
        catch (RuleSkippedException exception)
        {
            runs.Add(new(runId, metadata.RuleId, metadata.Version, metadata, context.Location, RuleRunStatus.Skipped,
                exception.ReasonCode, exception.Message, clock.Elapsed.TotalMilliseconds, Array.Empty<string>()) { NodeId = nodeId });
            Logger.Warning($"IMP-12: 规则跳过 {metadata.RuleId}，原因 {exception.ReasonCode}。");
        }
        catch (Exception exception)
        {
            string operation = $"RuleEngine.Execute:{metadata.RuleId}:v{metadata.Version}:{runId}";
            UnexpectedErrorReport? incident = ExceptionPolicy.IsExpected(exception) ? null : ExceptionPolicy.Capture(exception, operation, _unexpectedErrors);
            Logger.Error($"IMP-12: 规则失败 {metadata.RuleId}；{exception.GetType().Name}。");
            runs.Add(new(runId, metadata.RuleId, metadata.Version, metadata, context.Location, RuleRunStatus.Failed,
                "RULE_EXECUTION_FAILED", ExceptionPolicy.Message(exception), clock.Elapsed.TotalMilliseconds,
                Array.Empty<string>(), exception.GetType().FullName, incident) { NodeId = nodeId });
        }
    }

    private static PlanDiagnostic Merge(PlanDiagnostic first, PlanDiagnostic second)
    {
        // Merge only equal semantic identity/evidence; keep every originating invocation.
        var primary = string.CompareOrdinal(first.Origins[0].RuleId, second.Origins[0].RuleId) <= 0 ? first : second;
        string[] Union(IEnumerable<string> a, IEnumerable<string> b) => a.Concat(b).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var proposal = new DiagnosticProposal(primary.SemanticCode, primary.Title, primary.Summary,
            (IssueSeverity)Math.Max((int)first.Severity, (int)second.Severity))
        {
            Confidence = (DiagnosticConfidence)Math.Min((int)first.Confidence, (int)second.Confidence),
            Scope = primary.Scope,
            Evidence = primary.Evidence, Hypotheses = Union(first.Hypotheses, second.Hypotheses),
            Recommendations = Union(first.Recommendations, second.Recommendations), Applicability = Union(first.Applicability, second.Applicability),
            Limitations = Union(first.Limitations, second.Limitations), LegacyResult = primary.LegacyResult,
            LegacyExplanation = primary.LegacyExplanation
        };
        return new(first.DiagnosticId, proposal, first.Location,
            first.Origins.Concat(second.Origins).Distinct(), first.Objects.Concat(second.Objects).Distinct(), primary.NodeId);
    }

    private static XElement Temporary(XElement parent, XNamespace ns)
    {
        var element = new XElement(ns + "RelOp");
        parent.Add(element);
        return element;
    }
}
