using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

internal static class QueryPlanXml
{
    internal static bool IsInScope(XElement element, XNamespace ns) =>
        !element.AncestorsAndSelf().Any(ancestor => ancestor.Name.Namespace != ns || ancestor.Name.LocalName == "InternalInfo");

    internal static XElement? Find(XElement element, XNamespace ns) =>
        IsInScope(element, ns) ? element.AncestorsAndSelf(ns + "QueryPlan").FirstOrDefault() : null;

    internal static IEnumerable<XElement> Enumerate(XDocument document, XNamespace ns)
    {
        var model = PlanIdentityAdapter.GetDocument(document);
        return model != null ? model.QueryPlans.Select(query => model.GetQueryPlanSource(query.Key)!)
            : document.Descendants(ns + "QueryPlan").Where(query => IsInScope(query, ns));
    }

    // A QueryPlan owns its runtime information, not nested plans or opaque extensions.
    internal static IEnumerable<XElement> Descendants(XElement queryPlan, XNamespace ns)
    {
        var pending = new Stack<XElement>(queryPlan.Elements().Reverse());
        while (pending.TryPop(out var element))
        {
            if (element.Name.Namespace != ns || element.Name.LocalName is "QueryPlan" or "InternalInfo" or "Statements") continue;
            yield return element;
            foreach (var child in element.Elements().Reverse()) pending.Push(child);
        }
    }
}
