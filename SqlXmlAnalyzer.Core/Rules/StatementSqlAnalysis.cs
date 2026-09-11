using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules;

internal static class StatementSqlAnalysis
{
    internal static TSqlFragment? Parse(XElement element, XNamespace ns)
    {
        var statement = element.AncestorsAndSelf().FirstOrDefault(ancestor => ancestor.Name.Namespace == ns
            && Services.PlanDocumentBuilder.IsStatement(ancestor.Name.LocalName)
            && ancestor.Parent?.Name == ns + "Statements");
        string? sql = (string?)statement?.Attribute("StatementText");
        if (string.IsNullOrWhiteSpace(sql)) return null;
        using var reader = new StringReader(sql);
        var fragment = new TSql160Parser(true).Parse(reader, out var errors);
        return errors.Count == 0 ? fragment : null;
    }

    internal static int MaximumSelectListSubqueries(TSqlFragment fragment)
    {
        var visitor = new SelectListVisitor();
        fragment.Accept(visitor);
        return visitor.Maximum;
    }

    internal sealed record UnknownHints(bool AllParameters, IReadOnlySet<string> Parameters);

    internal static UnknownHints GetOptimizeForUnknown(TSqlFragment fragment)
    {
        var visitor = new UnknownHintVisitor();
        fragment.Accept(visitor);
        return new(visitor.AllParameters, visitor.Parameters);
    }

    private sealed class SelectListVisitor : TSqlFragmentVisitor
    {
        internal int Maximum { get; private set; }
        public override void ExplicitVisit(QuerySpecification node)
        {
            var counter = new ScalarSubqueryCounter();
            foreach (var expression in node.SelectElements.OfType<SelectScalarExpression>())
                expression.Expression.Accept(counter);
            Maximum = Math.Max(Maximum, counter.Count);
            base.ExplicitVisit(node);
        }
    }

    private sealed class ScalarSubqueryCounter : TSqlFragmentVisitor
    {
        internal int Count { get; private set; }
        // Nested subqueries are not additional expressions in the owner's SELECT list.
        public override void ExplicitVisit(ScalarSubquery node) => Count++;
    }

    private sealed class UnknownHintVisitor : TSqlFragmentVisitor
    {
        internal bool AllParameters { get; private set; }
        internal HashSet<string> Parameters { get; } = new(StringComparer.Ordinal);
        public override void ExplicitVisit(OptimizeForOptimizerHint node)
        {
            AllParameters |= node.IsForUnknown;
            foreach (var pair in node.Pairs.Where(pair => pair.IsForUnknown)) Parameters.Add(pair.Variable.Name);
        }
    }
}
