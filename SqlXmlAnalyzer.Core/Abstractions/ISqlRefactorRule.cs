using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Abstractions
{
    public record RuleResult(TSqlFragment Fragment, bool IsApplied, string? ChangeDescription);

    public interface ISqlRefactorRule
    {
        string RuleId { get; }
        string Name { get; }
        string Description { get; }
        int Priority { get; }

        bool CanApply(TSqlFragment fragment, RefactorContext context);
        RuleResult Apply(TSqlFragment fragment, RefactorContext context);
    }
}
