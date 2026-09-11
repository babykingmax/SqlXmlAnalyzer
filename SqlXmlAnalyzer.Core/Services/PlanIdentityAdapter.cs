using System.Runtime.CompilerServices;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services;

/// <summary>Bridges existing XDocument/XElement consumers to one identity model per source revision.</summary>
public static class PlanIdentityAdapter
{
    private static readonly ConditionalWeakTable<XDocument, Entry> Entries = new();

    public static void SetEnvelope(XDocument document, DocumentEnvelope envelope) =>
        Entries.GetValue(document, source => new Entry(source)).SetEnvelope(envelope);

    internal static DocumentEnvelope? GetRegisteredEnvelope(XDocument document) =>
        Entries.TryGetValue(document, out var entry) ? entry.GetEnvelope() : null;

    public static PlanDocument? GetDocument(XDocument? document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        XNamespace ns = InputRecognitionService.ShowPlanNamespace;
        if (document?.Root?.Name != ns + "ShowPlanXML" || !document.Root.Elements(ns + "BatchSequence").Any()) return null;
        return Entries.GetValue(document, source => new Entry(source)).Get(cancellationToken);
    }

    public static PlanOperator? GetOperator(XElement element) => GetDocument(element.Document)?.FindOperator(element);

    public static List<XElement> GetOperatorSources(XDocument document, XNamespace ns)
    {
        var model = GetDocument(document);
        return model == null ? document.Descendants(ns + "RelOp").ToList()
            : model.Operators.Select(op => model.GetOperatorSource(op.Key)!).ToList();
    }

    public static PlanQueryPlan? SelectQueryPlan(PlanDocument document, PlanQueryPlanKey? key)
    {
        if (key != null)
            return document.QueryPlans.FirstOrDefault(q => q.Key == key)
                ?? throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 所选 QueryPlan 不属于当前文档。");
        if (document.QueryPlans.Count > 1)
            throw new InvalidDataException("PLAN_SELECTION_REQUIRED: 多计划文档必须显式指定 QueryPlan 身份。");
        return document.QueryPlans.SingleOrDefault();
    }

    private sealed class Entry
    {
        private readonly XDocument _source;
        private readonly object _gate = new();
        private DocumentEnvelope _envelope;
        private PlanDocument? _model;
        public Entry(XDocument source)
        {
            _source = source;
            _envelope = FreshEnvelope();
            source.Changed += (_, _) =>
            {
                lock (_gate) { _model = null; _envelope = FreshEnvelope(); }
            };
        }
        private DocumentEnvelope FreshEnvelope() => new(Guid.NewGuid().ToString("N"), null, null,
            AnalysisDocumentKind.ExecutionPlanXml, null, (string?)_source.Root?.Attribute("Build"), (string?)_source.Root?.Attribute("Version"), null);
        public DocumentEnvelope GetEnvelope()
        {
            lock (_gate) return _envelope;
        }
        public void SetEnvelope(DocumentEnvelope envelope)
        {
            lock (_gate)
            {
                if (_envelope != envelope) { _envelope = envelope; _model = null; }
            }
        }
        public PlanDocument Get(CancellationToken cancellationToken)
        {
            lock (_gate) return _model ??= new PlanDocumentBuilder().Build(_source, _envelope, cancellationToken);
        }
    }
}
