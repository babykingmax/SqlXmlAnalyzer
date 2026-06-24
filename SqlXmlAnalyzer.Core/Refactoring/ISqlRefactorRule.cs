using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    internal interface ISqlRefactorRule
    {
        string RuleId { get; }
        string Name { get; }
        string Description { get; }
        int Priority { get; }

        bool CanApply(TSqlFragment fragment, SqlXmlAnalyzer.Core.RefactorContext context);
        TSqlFragment Apply(TSqlFragment fragment, SqlXmlAnalyzer.Core.RefactorContext context);
    }
}
