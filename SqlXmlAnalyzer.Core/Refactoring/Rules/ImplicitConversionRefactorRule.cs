using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Refactoring.Visitors;

namespace SqlXmlAnalyzer.Core.Refactoring.Rules
{
    public class ImplicitConversionRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_001_IMPLICIT_CONV";
        public string Name => "Implicit Conversion Fixer";
        public string Description => "Removes redundant N prefix from string literals compared to columns to prevent index scans.";
        public int Priority => 50;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            return true;
        }

        public TSqlFragment Apply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new ImplicitConversionVisitor();
            fragment.Accept(visitor);
            return fragment;
        }
    }
}
