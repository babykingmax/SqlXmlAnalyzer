using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Refactoring.Visitors;

namespace SqlXmlAnalyzer.Core.Refactoring.Rules
{
    internal class SargableRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_003_SARGABLE";
        public string Name => "Non-Sargable Function Optimizer";
        public string Description => "Rewrites non-sargable functions like LEFT or ISNULL to sargable expressions to enable index seeks.";
        public int Priority => 70;

        public bool CanApply(TSqlFragment fragment, SqlXmlAnalyzer.Core.RefactorContext context)
        {
            return true;
        }

        public TSqlFragment Apply(TSqlFragment fragment, SqlXmlAnalyzer.Core.RefactorContext context)
        {
            var visitor = new SargableVisitor(context);
            fragment.Accept(visitor);
            return fragment;
        }
    }
}
