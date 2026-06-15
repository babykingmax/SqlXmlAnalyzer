using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public interface ISqlRefactorRule
    {
        string RuleId { get; }
        string Name { get; }
        string Description { get; }
        int Priority { get; }

        bool CanApply(TSqlFragment fragment, RefactorContext context);
        TSqlFragment Apply(TSqlFragment fragment, RefactorContext context);
    }
}
