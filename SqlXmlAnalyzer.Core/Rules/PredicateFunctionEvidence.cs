using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Rules;

/// <summary>Recognizes column arguments, not function keywords in literals or identifiers.</summary>
internal static class PredicateFunctionEvidence
{
    internal static bool HasFunctionOnColumn(IReadOnlyList<string> predicates, CancellationToken token)
    {
        foreach (string predicate in predicates)
        {
            token.ThrowIfCancellationRequested();
            // ScalarString is untrusted, and is not always valid T-SQL. Failure to parse
            // this optional hint must not discard the measured residual observation.
            if (predicate.Length > 16_384)
            {
                Logger.Warning("IMP-14: 谓词超出函数提示解析长度限制，保留残差事实。");
                continue;
            }
            using var reader = new StringReader("SELECT 1 WHERE " + predicate);
            var fragment = new TSql160Parser(true).Parse(reader, out var errors);
            token.ThrowIfCancellationRequested();
            if (errors.Count > 0)
            {
                Logger.Warning("IMP-14: 谓词不满足 T-SQL 提示解析条件，保留残差事实。");
                continue;
            }
            var visitor = new FunctionVisitor();
            fragment.Accept(visitor);
            if (visitor.Found) return true;
        }
        return false;
    }

    private sealed class FunctionVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }
        private void Check(TSqlFragment fragment)
        {
            var columns = new ColumnVisitor();
            fragment.Accept(columns);
            Found |= columns.Found;
        }
        public override void ExplicitVisit(FunctionCall node)
        {
            if (node.FunctionName.Value.Equals("YEAR", StringComparison.OrdinalIgnoreCase)
                || node.FunctionName.Value.Equals("SUBSTRING", StringComparison.OrdinalIgnoreCase)
                || node.FunctionName.Value.Equals("ISNULL", StringComparison.OrdinalIgnoreCase)) Check(node);
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(ConvertCall node) { Check(node); base.ExplicitVisit(node); }
    }

    private sealed class ColumnVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }
        public override void ExplicitVisit(ColumnReferenceExpression node) => Found = true;
    }
}
