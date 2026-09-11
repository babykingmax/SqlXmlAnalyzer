using System.Globalization;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Comparison;

/// <summary>Local Sort/TopSort semantics; child operators supply their own signatures.</summary>
internal sealed record PlanComparisonSortEvidence(string Signature, bool Complete)
{
    internal static PlanComparisonSortEvidence Read(XElement source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ns = source.Name.Namespace;
        var sorts = source.Elements().Where(e => e.Name.LocalName is "Sort" or "TopSort").ToArray();
        if (sorts.Length == 0) return new("", (string?)source.Attribute("PhysicalOp") != "Sort"
            && (string?)source.Attribute("LogicalOp") is not ("Sort" or "Distinct Sort" or "TopN Sort"));
        if (sorts.Length != 1 || sorts[0].Name.Namespace != ns) return new("", false);
        var sort = sorts[0];
        bool complete = BooleanText((string?)sort.Attribute("Distinct")) != null;
        bool top = sort.Name.LocalName == "TopSort";
        complete &= (string?)source.Attribute("LogicalOp") != "TopN Sort" || top;
        if (top)
        {
            complete &= NonnegativeInteger((string?)sort.Attribute("Rows")) != null;
            complete &= sort.Attribute("WithTies") == null || BooleanText((string?)sort.Attribute("WithTies")) != null;
        }
        var orders = sort.Elements(ns + "OrderBy").ToArray();
        complete &= orders.Length == 1;
        foreach (var order in orders)
        {
            var entries = order.Elements().ToArray();
            complete &= entries.Length > 0;
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                var columns = entry.Elements().ToArray();
                complete &= entry.Name == ns + "OrderByColumn" && BooleanText((string?)entry.Attribute("Ascending")) != null
                    && columns.Length == 1 && ValidColumn(columns[0], ns);
            }
        }
        var partitions = sort.Elements(ns + "PartitionId").ToArray();
        complete &= partitions.Length <= 1 && partitions.All(p => p.Elements().Count() == 1 && ValidColumn(p.Elements().First(), ns));
        complete &= sort.Elements().All(e => e.Name.Namespace == ns
            && e.Name.LocalName is "OrderBy" or "PartitionId" or "RelOp" or "DefinedValues" or "InternalInfo");

        // Preserve key order and optional fields. Do not fingerprint costs, runtime or child NodeIds.
        var evidence = new XElement(sort.Name,
            sort.Attributes().Where(a => a.Name.Namespace == XNamespace.None && a.Name.LocalName is "Distinct" or "Rows" or "WithTies"),
            sort.Elements().Where(e => e.Name.LocalName is "OrderBy" or "PartitionId"));
        foreach (var attribute in evidence.DescendantsAndSelf().Attributes())
        {
            token.ThrowIfCancellationRequested();
            if (attribute.Name == "Distinct" || attribute.Name == "Ascending" || attribute.Name == "WithTies")
                attribute.Value = BooleanText(attribute.Value) ?? attribute.Value;
            else if (attribute.Parent == evidence && attribute.Name == "Rows")
                attribute.Value = NonnegativeInteger(attribute.Value) ?? attribute.Value;
        }
        return new(PlanComparisonPredicateEvidence.SignatureOf(evidence, token), complete);
    }

    private static bool ValidColumn(XElement column, XNamespace ns) => column.Name == ns + "ColumnReference"
        && !string.IsNullOrWhiteSpace((string?)column.Attribute("Column"))
        && column.Elements().All(e => e.Name == ns + "ScalarOperator")
        && PlanComparisonPredicateEvidence.HasCompleteExpressions(column, ns);

    private static string? BooleanText(string? value) => value?.Trim() switch { "true" or "1" => "true", "false" or "0" => "false", _ => null };
    private static string? NonnegativeInteger(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
        && number >= 0 ? number.ToString(CultureInfo.InvariantCulture) : null;
}
