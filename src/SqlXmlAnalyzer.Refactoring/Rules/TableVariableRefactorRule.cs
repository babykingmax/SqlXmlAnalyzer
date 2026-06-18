using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Refactoring.Rules
{
    public class TableVariableRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_002_TABLE_VAR";
        public string Name => "Table Variable to Temp Table";
        public string Description => "Converts table variables (@Table) to temp tables (#Table) to improve query optimization and parallel execution.";
        public int Priority => 60;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            var collector = new TableVariableDeclarationCollector();
            fragment.Accept(collector);
            return collector.Declarations.Count > 0;
        }

        public RuleResult Apply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new TableVariableVisitor(context);
            fragment.Accept(visitor);
            if (visitor.Changed)
            {
                return new RuleResult(fragment, true, "Converted table variables to temp tables");
            }
            return new RuleResult(fragment, false, null);
        }
    }
}
