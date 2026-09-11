using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services;

public interface IPlanDocumentBuilder
{
    PlanDocument Build(XDocument document, DocumentEnvelope envelope, CancellationToken cancellationToken = default);
}

public sealed class PlanDocumentBuilder(IUnexpectedErrorReporter? unexpectedErrors = null) : IPlanDocumentBuilder
{
    public PlanDocument Build(XDocument document, DocumentEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(envelope);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            XNamespace ns = InputRecognitionService.ShowPlanNamespace;
            if (document.Root?.Name != ns + "ShowPlanXML")
                throw new InvalidDataException("INPUT_EXPECTED_PLAN: 身份模型需要标准 ShowPlanXML。");
            Logger.Debug("IMP-10: 开始构建计划层级身份。");
            var source = new DocumentSource(cancellationToken);
            var locations = new Dictionary<XElement, PlanLocation>();
            var operatorSources = new Dictionary<PlanOperatorKey, XElement>();
            var queryPlanSources = new Dictionary<PlanQueryPlanKey, XElement>();
            var diagnostics = new List<InputDiagnostic>();
            var batches = new List<PlanBatch>();
            locations[document.Root] = new(envelope.DocumentId, null, null, null, null, source.Location(document.Root));
            foreach (XElement batch in document.Root.Elements(ns + "BatchSequence").Elements(ns + "Batch"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batchKey = new PlanBatchKey(envelope.DocumentId, batches.Count + 1);
                var batchLocation = new PlanLocation(envelope.DocumentId, batchKey, null, null, null, source.Location(batch));
                locations[batch] = batchLocation;
                var statements = new List<StatementBuilder>();
                var pending = new Stack<(XElement Element, StatementBuilder? Statement, QueryBuilder? Query, OperatorBuilder? Operator)>();
                PushChildren(batch, null, null, null);
                while (pending.TryPop(out var item))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    XElement element = item.Element;
                    if (element.Name.Namespace != ns || element.Name.LocalName == "InternalInfo") continue;
                    var statement = item.Statement;
                    var query = item.Query;
                    var op = item.Operator;
                    string name = element.Name.LocalName;
                    if (element.Parent?.Name == ns + "Statements" && IsStatement(name))
                    {
                        var key = new PlanStatementKey(batchKey, statements.Count + 1);
                        var location = new PlanLocation(envelope.DocumentId, batchKey, key, null, null, source.Location(element));
                        statement = new(key, statement?.Key, element, location);
                        statements.Add(statement);
                        locations[element] = location;
                        query = null;
                        op = null;
                    }
                    else if (name == "QueryPlan" && statement != null)
                    {
                        var key = new PlanQueryPlanKey(statement.Key, statement.Queries.Count + 1);
                        var location = new PlanLocation(envelope.DocumentId, batchKey, statement.Key, key, null, source.Location(element));
                        query = new(key, location);
                        statement.Queries.Add(query);
                        queryPlanSources[key] = element;
                        locations[element] = location;
                        op = null;
                    }
                    else if (name == "RelOp" && query != null)
                    {
                        string? rawNodeId = (string?)element.Attribute("NodeId");
                        string? nodeId = null;
                        string? nodeIdDiagnostic = null;
                        if (string.IsNullOrWhiteSpace(rawNodeId)) nodeIdDiagnostic = "PLAN_NODE_ID_MISSING";
                        // RelOpType.NodeId is xsd:int. Canonical text bounds every key and
                        // parent reference even when the source contains many leading zeros.
                        else if (int.TryParse(rawNodeId.AsSpan().Trim(" \t\r\n"), NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture, out int value))
                            nodeId = value.ToString(CultureInfo.InvariantCulture);
                        else nodeIdDiagnostic = "PLAN_NODE_ID_INVALID";
                        var key = new PlanOperatorKey(query.Key, nodeId, query.Operators.Count + 1);
                        var location = new PlanLocation(envelope.DocumentId, batchKey, statement!.Key, query.Key, key, source.Location(element));
                        op = new(key, op?.Key, element, location);
                        query.Operators.Add(op);
                        operatorSources[key] = element;
                        locations[element] = location;
                        if (nodeId != null && !query.NodeIds.Add(nodeId)) nodeIdDiagnostic = "PLAN_NODE_ID_DUPLICATE";
                        if (nodeIdDiagnostic != null)
                            diagnostics.Add(new(nodeIdDiagnostic,
                                "算子 NodeId 缺失、无效或重复；使用源顺序消歧，原值保留在源 XML 中。", location.Source));
                    }
                    else if (name == "Object" && op != null)
                    {
                        var location = new PlanLocation(envelope.DocumentId, batchKey, statement!.Key, query!.Key, op.Key, source.Location(element));
                        op.Objects.Add(ReadObject(element, location));
                        locations[element] = location;
                    }
                    else if (name == "MissingIndex" && query != null)
                    {
                        locations[element] = new(envelope.DocumentId, batchKey, statement!.Key, query.Key, null, source.Location(element));
                    }
                    PushChildren(element, statement, query, op);
                }
                batches.Add(new(batchKey, batchLocation, statements.Select(s => s.Freeze(cancellationToken)).ToList().AsReadOnly()));

                void PushChildren(XElement parent, StatementBuilder? statement, QueryBuilder? query, OperatorBuilder? op)
                {
                    foreach (XElement child in parent.Elements().Reverse())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        pending.Push((child, statement, query, op));
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostics.Count > 0) Logger.Warning($"IMP-10: {diagnostics.Count} 个算子需要按源顺序消歧。");
            var result = new PlanDocument(envelope, batches.AsReadOnly(), diagnostics.AsReadOnly(), locations, operatorSources, queryPlanSources);
            Logger.Debug($"IMP-10: 身份模型已构建；批次 {batches.Count}，语句 {result.Statements.Count}，算子 {result.Operators.Count}。");
            return result;
        }
        catch (Exception exception)
        {
            ExceptionPolicy.Describe(exception, "PlanDocumentBuilder.Build", unexpectedErrors);
            throw;
        }
    }

    public static bool IsStatement(string name) => name is "StmtSimple" or "StmtCond" or "StmtCursor" or "StmtReceive" or "StmtUseDb";

    public static SqlObjectReference ReadObject(XElement element, PlanLocation location)
    {
        string? Part(string name) => SqlObjectIdentity.DecodeIdentifier((string?)element.Attribute(name));
        return new(new(Part("Server"), Part("Database"), Part("Schema"), Part("Table") ?? Part("Object")),
            Part("Alias"), Part("Index"), (string?)element.Attribute("TableReferenceId"), location,
            new ReadOnlyDictionary<string, string>(element.Attributes().ToDictionary(a => a.Name.ToString(), a => a.Value)));
    }

    private sealed class StatementBuilder(PlanStatementKey key, PlanStatementKey? parent, XElement element, PlanLocation location)
    {
        public PlanStatementKey Key { get; } = key;
        public List<QueryBuilder> Queries { get; } = new();
        public PlanStatement Freeze(CancellationToken cancellationToken) => new(Key, parent, element.Name.LocalName, (string?)element.Attribute("StatementId"),
            (string?)element.Attribute("QueryHash"), (string?)element.Attribute("QueryPlanHash"), (string?)element.Attribute("StatementText"),
            location, Queries.Select(q => q.Freeze(cancellationToken)).ToList().AsReadOnly());
    }
    private sealed class QueryBuilder(PlanQueryPlanKey key, PlanLocation location)
    {
        public PlanQueryPlanKey Key { get; } = key;
        public List<OperatorBuilder> Operators { get; } = new();
        public HashSet<string> NodeIds { get; } = new(StringComparer.Ordinal);
        public PlanQueryPlan Freeze(CancellationToken cancellationToken) => new(Key, location, Operators.Select(o => o.Freeze(cancellationToken)).ToList().AsReadOnly());
    }
    private sealed class OperatorBuilder(PlanOperatorKey key, PlanOperatorKey? parent, XElement element, PlanLocation location)
    {
        public PlanOperatorKey Key { get; } = key;
        public List<SqlObjectReference> Objects { get; } = new();
        public PlanOperator Freeze(CancellationToken cancellationToken) => new(Key, parent, (string?)element.Attribute("PhysicalOp"), (string?)element.Attribute("LogicalOp"), location, Objects.AsReadOnly())
        {
            Facts = PlanOperatorFactsService.Get(element, element.Name.Namespace, cancellationToken)
        };
    }
}
