using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

/// <summary>One content-ownership contract for recognition and diagnostic entry points.</summary>
internal static class PlanCapabilityService
{
    private static readonly ConditionalWeakTable<XElement, Entry> Entries = new();

    public static DocumentCapabilities Get(XElement source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        XElement root = source.Document?.Root ?? source;
        return Entries.GetValue(root, element => new Entry(element)).Get(cancellationToken);
    }

    private static DocumentCapabilities Read(XElement root, CancellationToken cancellationToken)
    {
        XNamespace ns = root.Name.Namespace;
        bool standardPlan = root.Name == XName.Get("ShowPlanXML", InputRecognitionService.ShowPlanNamespace);
        var capabilities = DocumentCapabilities.PreservedSource;
        var pending = new Stack<(XElement Element, bool Statement, bool Query)>();
        pending.Push((root, false, false));
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, statement, query) = item;
            if (element.Name.Namespace != ns || element.Name.LocalName == "InternalInfo") continue;
            if (element.Name == ns + "Statements" && (!standardPlan ||
                element.Parent?.Name == ns + "Batch" && element.Parent.Parent?.Name == ns + "BatchSequence"
                && element.Parent.Parent.Parent == root))
                capabilities |= DocumentCapabilities.PlanStatements; // Empty Statements is supported content too.
            if (element.Parent?.Name == ns + "Statements" && PlanDocumentBuilder.IsStatement(element.Name.LocalName))
            {
                statement = true;
                query = false;
            }
            else if (element.Name == ns + "QueryPlan" && statement) query = true;

            if (!standardPlan || query)
            {
                if (element.Name == ns + "RelOp") capabilities |= DocumentCapabilities.PlanOperators;
                if (element.Name == ns + "RunTimeCountersPerThread" && element.Parent?.Name == ns + "RunTimeInformation"
                    && element.Parent.Parent?.Name == ns + "RelOp")
                    capabilities |= DocumentCapabilities.RuntimeCounters;
            }
            foreach (var child in element.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                pending.Push((child, statement, query));
            }
        }
        Logger.Debug($"PlanCapabilityService: 已按内容归属推导能力 {capabilities}。");
        return capabilities;
    }

    private sealed class Entry
    {
        private readonly XElement _source;
        private readonly object _gate = new();
        private DocumentCapabilities? _capabilities;

        public Entry(XElement source)
        {
            _source = source;
            source.Changed += (_, _) => { lock (_gate) _capabilities = null; };
        }

        public DocumentCapabilities Get(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _capabilities ??= Read(_source, cancellationToken);
            }
        }
    }
}
