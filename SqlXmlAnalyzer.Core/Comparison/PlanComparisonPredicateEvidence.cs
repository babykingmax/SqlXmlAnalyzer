using System.Text;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Comparison;

/// <summary>Preserves local predicate roles, range columns, bounds and ordered expression trees.</summary>
internal sealed record PlanComparisonPredicateEvidence(string Signature, bool Complete)
{
    internal static PlanComparisonPredicateEvidence Read(XElement source, CancellationToken token)
    {
        var ns = source.Name.Namespace;
        var predicates = new List<string>();
        bool complete = true;
        var pending = new Stack<XElement>(source.Elements().Reverse());
        while (pending.TryPop(out var element))
        {
            token.ThrowIfCancellationRequested();
            if (element.Name.Namespace != ns)
            {
                if (IsPredicateName(element.Name.LocalName)) complete = false;
                continue;
            }
            if (element.Name.LocalName is
                "RelOp" or "QueryPlan" or "Statements" or "Warnings" or "RunTimeInformation" or "OutputList" or "InternalInfo") continue;
            if (IsPredicateName(element.Name.LocalName))
            {
                predicates.Add(SignatureOf(element, token));
                complete &= IsComplete(element, ns);
                continue;
            }
            foreach (var child in element.Elements().Reverse()) pending.Push(child);
        }
        if ((string?)source.Attribute("PhysicalOp") is "Index Seek" or "Clustered Index Seek" && predicates.Count == 0) complete = false;
        return new(string.Join("", predicates.Order(StringComparer.Ordinal).Select(Frame)), complete);
    }

    private static bool IsPredicateName(string name) => name is "Predicate" or "BuildResidual" or "ProbeResidual" or "Residual"
        or "SeekPredicates" or "SeekPredicate" or "SeekPredicateNew" or "SeekPredicatePart";

    private static bool IsComplete(XElement predicate, XNamespace ns)
    {
        if (!HasCompleteExpressions(predicate, ns)) return false;
        var nodes = predicate.DescendantsAndSelf().ToArray();
        if (predicate.Name.LocalName is "Predicate" or "BuildResidual" or "ProbeResidual" or "Residual")
            return predicate.Elements().Count() == 1 && predicate.Elements(ns + "ScalarOperator").Count() == 1;
        var ranges = nodes.Where(e => e.Name.LocalName is "Prefix" or "StartRange" or "EndRange").ToArray();
        var nullChecks = nodes.Where(e => e.Name.LocalName == "IsNotNull").ToArray();
        if (ranges.Length == 0 && nullChecks.Length == 0) return false;
        if (nodes.Any(e => e.Name.LocalName is "SeekPredicates" or "SeekPredicatePart" or "SeekPredicateNew" or "SeekKeys" or "SeekPredicate"
            && !e.HasElements)) return false;
        foreach (var range in ranges)
        {
            if ((string?)range.Attribute("ScanType") is not ("EQ" or "GE" or "GT" or "LE" or "LT" or "NE"
                or "IS" or "IS NOT" or "IS NULL" or "IS NOT NULL" or "BINARY IS" or "BOTH NULL" or "ONE NULL")) return false;
            var columns = range.Elements(ns + "RangeColumns").ToArray();
            var expressions = range.Elements(ns + "RangeExpressions").ToArray();
            if (columns.Length != 1 || expressions.Length != 1) return false;
            var columnItems = columns[0].Elements().ToArray();
            var expressionItems = expressions[0].Elements().ToArray();
            if (columnItems.Length == 0 || columnItems.Length != expressionItems.Length
                || columnItems.Any(e => e.Name != ns + "ColumnReference" || string.IsNullOrWhiteSpace((string?)e.Attribute("Column")))
                || expressionItems.Any(e => e.Name != ns + "ScalarOperator")) return false;
        }
        return nullChecks.All(e => e.Elements(ns + "ColumnReference").Count() == 1
            && !string.IsNullOrWhiteSpace((string?)e.Element(ns + "ColumnReference")!.Attribute("Column")));
    }

    internal static bool HasCompleteExpressions(XElement source, XNamespace ns)
    {
        var nodes = source.DescendantsAndSelf().ToArray();
        if (nodes.Any(e => e.Name.Namespace != ns || e.Name.LocalName is "InternalInfo" or "RelOp" or "QueryPlan" or "Statements")) return false;
        if (nodes.Any(e => e.Name.LocalName == "ColumnReference" && string.IsNullOrWhiteSpace((string?)e.Attribute("Column")))) return false;
        var scalars = nodes.Where(e => e.Name.LocalName == "ScalarOperator").ToArray();
        if (scalars.Any(e => !e.HasElements && string.IsNullOrWhiteSpace((string?)e.Attribute("ScalarString")))) return false;
        foreach (var scalar in scalars.Where(e => e.HasElements))
        {
            var expressions = scalar.Elements().ToArray();
            if (expressions.Length != 1 || expressions[0].Name.LocalName is not ("Aggregate" or "Arithmetic" or "Assign" or "Compare"
                or "Const" or "Convert" or "Identifier" or "IF" or "Intrinsic" or "Logical" or "MultipleAssign" or "ScalarExpressionList"
                or "Sequence" or "Subquery" or "UDTMethod" or "UserDefinedAggregate" or "UserDefinedFunction")) return false;
            if (expressions[0].Name.LocalName == "Const" && string.IsNullOrWhiteSpace((string?)expressions[0].Attribute("ConstValue"))) return false;
            if (expressions[0].Name.LocalName == "Identifier" && (expressions[0].Elements().Count() != 1
                || expressions[0].Elements(ns + "ColumnReference").Count() != 1)) return false;
            if (expressions[0].Name.LocalName == "Compare")
            {
                int operands = (string?)expressions[0].Attribute("CompareOp") switch
                {
                    "IS NULL" or "IS NOT NULL" => 1,
                    "BINARY IS" or "BOTH NULL" or "EQ" or "GE" or "GT" or "IS" or "IS NOT" or "LE" or "LT" or "NE" or "ONE NULL" => 2,
                    _ => 0
                };
                if (operands == 0 || expressions[0].Elements().Count() != operands
                    || expressions[0].Elements(ns + "ScalarOperator").Count() != operands) return false;
            }
        }
        return true;
    }

    private static string Frame(string text) => text.Length + ":" + text;

    internal static string SignatureOf(XElement element, CancellationToken token)
    {
        var text = new StringBuilder();
        Append(element, text, token);
        return text.ToString();
    }

    private static void Append(XElement element, StringBuilder text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        text.Append(Frame(element.Name.ToString()));
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration
            // ScalarString is a rendering of the typed expression when that expression is present.
            && !(a.Name == "ScalarString" && element.HasElements)).OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToArray();
        text.Append(attributes.Length).Append(':');
        foreach (var attribute in attributes)
            text.Append(Frame(attribute.Name.ToString())).Append(Frame(attribute.Value));
        var children = element.Elements().ToArray();
        text.Append(children.Length).Append(':');
        foreach (var child in children) Append(child, text, token);
        // Preserve unexpected text as evidence, rather than silently erasing it.
        text.Append(Frame(string.Concat(element.Nodes().OfType<XText>().Where(t => !string.IsNullOrWhiteSpace(t.Value)).Select(t => t.Value))));
    }
}
