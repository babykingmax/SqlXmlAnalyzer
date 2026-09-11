using System.Xml;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Reporting;

internal static class PlanReportXml
{
    internal static string Select(XDocument document, PlanDocument model, PlanStatementKey? statement,
        PlanQueryPlanKey? queryPlan, ReportSizeBudget budget, CancellationToken token)
    {
        if (statement == null) return budget.Xml(document);
        XElement original = model.GetStatementSource(statement)!;
        // The Showplan schema requires exactly two operations for RECEIVE. A single-operation
        // projection cannot remain a valid StmtReceive without fabricating or leaking another plan.
        if (queryPlan != null && original.Name == document.Root!.Name.Namespace + "StmtReceive")
            throw new InvalidDataException("REPORT_SCOPE_NOT_REPRESENTABLE: RECEIVE 计划必须保留完整的两个操作，请导出整个语句或文档。");
        var excluded = new HashSet<XElement>();
        foreach (var other in model.Statements)
        {
            token.ThrowIfCancellationRequested();
            if (other.Key != statement) excluded.Add(model.GetStatementSource(other.Key)!);
        }
        if (queryPlan != null)
            foreach (var other in model.QueryPlans.Where(q => q.Key.Statement == statement && q.Key != queryPlan))
            {
                XElement source = model.GetQueryPlanSource(other.Key)!;
                // Cursor operations require their QueryPlan. Drop the whole unselected operation.
                excluded.Add(source.Parent?.Name == source.Name.Namespace + "Operation" ? source.Parent : source);
            }

        using var text = new ReportTextWriter(budget.Remaining, token);
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true }))
        {
            Start(document.Root!, writer);
            string ns = document.Root!.Name.NamespaceName;
            writer.WriteStartElement("BatchSequence", ns); writer.WriteStartElement("Batch", ns); writer.WriteStartElement("Statements", ns);
            Copy(original, writer);
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        }
        string xml = text.ToString(); budget.Value(xml); return xml;

        void Copy(XNode node, XmlWriter writer)
        {
            token.ThrowIfCancellationRequested();
            if (node is not XElement element) { node.WriteTo(writer); return; }
            if (excluded.Contains(element)) return;
            Start(element, writer);
            foreach (var child in element.Nodes()) Copy(child, writer);
            if (element.IsEmpty) writer.WriteEndElement(); else writer.WriteFullEndElement();
        }
    }

    private static void Start(XElement element, XmlWriter writer)
    {
        writer.WriteStartElement(element.GetPrefixOfNamespace(element.Name.Namespace), element.Name.LocalName, element.Name.NamespaceName);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                writer.WriteAttributeString(attribute.Name.LocalName == "xmlns" ? null : "xmlns", attribute.Name.LocalName,
                    "http://www.w3.org/2000/xmlns/", attribute.Value);
            else
                writer.WriteAttributeString(element.GetPrefixOfNamespace(attribute.Name.Namespace), attribute.Name.LocalName,
                    attribute.Name.NamespaceName, attribute.Value);
        }
    }
}
