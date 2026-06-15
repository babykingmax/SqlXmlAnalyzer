using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public interface ISqlRefactorRule
    {
        string RuleId { get; }
        string Name { get; }
        string Description { get; }
        
        /// <summary>
        /// Modifies the AST fragment in place.
        /// </summary>
        void Apply(TSqlFragment fragment);
    }
}
