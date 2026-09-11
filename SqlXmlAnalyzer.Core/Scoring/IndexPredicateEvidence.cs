using System.Xml.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Scoring;

internal sealed class IndexPredicateEvidence
{
    public HashSet<string> Equality { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Inequality { get; } = new(StringComparer.Ordinal);
    public bool UsedTextFallback { get; private set; }

    public static IndexPredicateEvidence Read(IEnumerable<XElement> operators, SqlObjectIdentity target, XNamespace ns)
    {
        var result = new IndexPredicateEvidence();
        foreach (var op in operators)
        {
            var local = IndexTargetResolver.LocalElements(op, ns).ToArray();
            var entityNames = local.Where(e => e.Name == ns + "ColumnReference"
                    && IndexTargetResolver.TryReadEntityColumn(e, target, out _))
                .Select(e => (string)e.Attribute("Column")!).ToHashSet(StringComparer.Ordinal);
            var parameters = local.Where(e => e.Parent?.Name == ns + "Identifier")
                .Concat(op.Ancestors(ns + "QueryPlan").FirstOrDefault()?.Elements(ns + "ParameterList")
                    .Elements(ns + "ColumnReference") ?? Enumerable.Empty<XElement>())
                .Where(e => IsParameter(e, ns)).Select(e => (string)e.Attribute("Column")!).ToHashSet(StringComparer.Ordinal);

            // A filter is a complete scalar root owned by this access payload. RangeExpressions,
            // CASE branches, subqueries and descendant ScalarStrings are not filter conjuncts.
            foreach (var predicate in local.Where(e => e.Name == ns + "Predicate" && e.Parent?.Parent == op))
            {
                var roots = predicate.Elements().ToArray();
                if (roots.Length != 1 || roots[0].Name != ns + "ScalarOperator") continue;
                var root = roots[0];
                if (root.HasElements) result.ReadStructured(root, target, ns);
                else if ((string?)root.Attribute("ScalarString") is { } text)
                    result.ReadText(text, target, entityNames, parameters);
            }
        }
        return result;
    }

    private void ReadStructured(XElement root, SqlObjectIdentity target, XNamespace ns)
    {
        var pending = new Stack<XElement>();
        pending.Push(root);
        while (pending.TryPop(out var scalar))
        {
            var payload = Payload(scalar, ns);
            if (payload?.Name == ns + "Logical" && (string?)payload.Attribute("Operation") == "AND")
            {
                var args = payload.Elements().ToArray();
                if (args.Length > 0 && args.All(e => e.Name == ns + "ScalarOperator"))
                    foreach (var argument in args) pending.Push(argument);
            }
            else if (payload?.Name == ns + "Compare") ReadComparison(payload, target, ns);
            // Deliberately stop at OR, NOT, IF, Convert, Intrinsic, and all other scalar operators.
        }
    }

    private void ReadComparison(XElement compare, SqlObjectIdentity target, XNamespace ns)
    {
        var args = compare.Elements().ToArray();
        if (args.Any(e => e.Name != ns + "ScalarOperator")) return;
        string? kind = (string?)compare.Attribute("CompareOp");
        string? Column(XElement scalar)
        {
            var reference = ColumnReference(scalar, ns);
            return reference != null && IndexTargetResolver.TryReadEntityColumn(reference, target, out string name) ? name : null;
        }
        if (kind is "IS NULL" or "IS NOT NULL")
        {
            if (args.Length == 1 && Column(args[0]) is { } name)
                (kind == "IS NULL" ? Equality : Inequality).Add(name);
            return;
        }
        if (args.Length != 2) return;
        for (int side = 0; side < 2; side++)
        {
            if (Column(args[side]) is not { } name) continue;
            string? constant = Constant(args[1 - side], ns);
            if (kind is "IS" or "IS NOT")
            {
                if (constant != null && IsNull(constant)) (kind == "IS" ? Equality : Inequality).Add(name);
                continue;
            }
            // Ordinary comparisons to NULL are not IS NULL tests; do not guess ANSI_NULLS semantics.
            bool value = constant != null && !IsNull(constant)
                || ColumnReference(args[1 - side], ns) is { } parameter && IsParameter(parameter, ns);
            if (!value) continue;
            if (kind == "EQ") Equality.Add(name);
            else if (kind is "LT" or "LE" or "GT" or "GE" or "NE") Inequality.Add(name);
        }
    }

    private static XElement? Payload(XElement scalar, XNamespace ns)
    {
        if (scalar.Name != ns + "ScalarOperator") return null;
        var children = scalar.Elements().ToArray();
        int count = children.Length;
        if (count > 0 && children[^1].Name == ns + "InternalInfo") count--;
        return count == 1 && children[0].Name.Namespace == ns ? children[0] : null;
    }

    private static XElement? ColumnReference(XElement scalar, XNamespace ns)
    {
        var payload = Payload(scalar, ns);
        if (payload?.Name != ns + "Identifier") return null;
        var children = payload.Elements().ToArray();
        return children.Length == 1 && children[0].Name == ns + "ColumnReference" ? children[0] : null;
    }

    private static string? Constant(XElement scalar, XNamespace ns)
    {
        var payload = Payload(scalar, ns);
        return payload?.Name == ns + "Const" && !payload.HasElements && (string?)payload.Attribute("ConstValue") is { Length: > 0 } value ? value : null;
    }

    private static bool IsParameter(XElement reference, XNamespace ns) => reference.Name == ns + "ColumnReference"
        && ((string?)reference.Attribute("Column")) is { Length: > 1 } name && name.StartsWith('@')
        && new[] { "Server", "Database", "Schema", "Table" }.All(part => reference.Attribute(part) == null);

    private static bool IsNull(string value)
    {
        var text = value.AsSpan().Trim();
        while (text.Length >= 2 && text[0] == '(' && text[^1] == ')') text = text[1..^1].Trim();
        return text.Equals("NULL", StringComparison.OrdinalIgnoreCase);
    }

    private void ReadText(string expression, SqlObjectIdentity target, HashSet<string> entities, HashSet<string> parameters)
    {
        using var reader = new StringReader("SELECT 1 WHERE " + expression);
        var fragment = new TSql160Parser(true).Parse(reader, out var errors);
        if (errors.Count != 0 || fragment is not TSqlScript script || script.Batches.Count != 1
            || script.Batches[0].Statements.Count != 1 || script.Batches[0].Statements[0] is not SelectStatement statement
            || statement.QueryExpression is not QuerySpecification query || query.FromClause != null || query.WhereClause == null) return;
        UsedTextFallback = true;
        var pending = new Stack<BooleanExpression>();
        pending.Push(query.WhereClause.SearchCondition);
        while (pending.TryPop(out var predicate))
        {
            switch (predicate)
            {
                case BooleanParenthesisExpression parenthesis:
                    pending.Push(parenthesis.Expression);
                    break;
                case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and:
                    pending.Push(and.FirstExpression);
                    pending.Push(and.SecondExpression);
                    break;
                case BooleanIsNullExpression nullTest:
                    if (Column(nullTest.Expression) is { } nullColumn) (nullTest.IsNot ? Inequality : Equality).Add(nullColumn);
                    break;
                case BooleanComparisonExpression comparison:
                    string? name = Value(comparison.SecondExpression) ? Column(comparison.FirstExpression)
                        : Value(comparison.FirstExpression) ? Column(comparison.SecondExpression) : null;
                    if (name == null) break;
                    if (comparison.ComparisonType == BooleanComparisonType.Equals) Equality.Add(name);
                    else if (comparison.ComparisonType is BooleanComparisonType.GreaterThan or BooleanComparisonType.GreaterThanOrEqualTo
                        or BooleanComparisonType.LessThan or BooleanComparisonType.LessThanOrEqualTo or BooleanComparisonType.NotEqualToBrackets
                        or BooleanComparisonType.NotEqualToExclamation) Inequality.Add(name);
                    break;
            }
        }

        string? Column(ScalarExpression scalar)
        {
            if (Unwrap(scalar) is not ColumnReferenceExpression column || column.MultiPartIdentifier == null) return null;
            var parts = column.MultiPartIdentifier.Identifiers.Select(id => id.Value).ToArray();
            if (parts.Length is 0 or > 5 || parts.Length == 1 && parts[0].StartsWith('@') && !entities.Contains(parts[0])) return null;
            string?[] expected = [target.Server, target.Database, target.Schema, target.Object];
            for (int offset = 2; offset <= parts.Length; offset++)
                if (parts[^offset] != expected[5 - offset]) return null;
            return parts[^1];
        }
        bool Value(ScalarExpression scalar)
        {
            var value = Unwrap(scalar);
            if (value is NullLiteral) return false;
            if (value is Literal or VariableReference) return true;
            if (value is ColumnReferenceExpression column && column.MultiPartIdentifier?.Identifiers.Count == 1)
            {
                string name = column.MultiPartIdentifier.Identifiers[0].Value;
                return parameters.Contains(name) && !entities.Contains(name);
            }
            // Unwrap signs only on numeric constants, never on columns, functions or subqueries.
            if (value is not UnaryExpression) return false;
            while (value is UnaryExpression unary && unary.UnaryExpressionType is UnaryExpressionType.Positive or UnaryExpressionType.Negative)
                value = Unwrap(unary.Expression);
            return value is IntegerLiteral or NumericLiteral or RealLiteral;
        }
    }

    private static ScalarExpression Unwrap(ScalarExpression expression)
    {
        while (expression is ParenthesisExpression parenthesis) expression = parenthesis.Expression;
        return expression;
    }
}
