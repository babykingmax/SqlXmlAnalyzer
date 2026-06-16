using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Refactoring.Visitors;

namespace SqlXmlAnalyzer.Core.Refactoring.Rules
{
    public class TableVariableRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_002_TABLE_VAR";
        public string Name => "Table Variable to Temp Table";
        public string Description => "Converts table variables (@Table) to temp tables (#Table) to improve query optimization and parallel execution.";
        public int Priority => 60;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            return true;
        }

        public TSqlFragment Apply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new TableVariableVisitor(context);
            fragment.Accept(visitor);
            return fragment;
        }
    }
}
