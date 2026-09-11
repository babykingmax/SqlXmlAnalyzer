using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class MultipleScalarSubqueriesRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_024_SCALAR_SUBQUERY_PATTERN";
        public string Name => "Multiple Scalar Subqueries Detection";
        public string Description => "Detects multiple scalar subqueries in the SELECT clause.";

        public RuleEvaluation Evaluate(RuleAnalysisContext context)
        {
            if (StatementSqlAnalysis.Parse(context.LegacyElement, context.Namespace) == null)
                return RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "StatementText 缺失或无法解析，不能检查 SELECT 列表中的标量子查询。");
            return LegacyDiagnosticAdapter.Evaluate(this, context);
        }

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            var fragment = StatementSqlAnalysis.Parse(relOp, ns);
            if (fragment == null) return null;
            int scalarCount = StatementSqlAnalysis.MaximumSelectListSubqueries(fragment);
            if (scalarCount < 2) return null;
            return new AnalysisResult
            {
                RuleId = RuleId,
                Severity = "Warning",
                Title = "标量子查询反模式",
                Message = $"SELECT 列表中检测到 {scalarCount} 个标量子查询。请检查执行计划中是否存在重复访问和较高的实际执行次数；仅凭语法不能认定逐行执行或性能问题。\n可评估 JOIN / APPLY 或合并计算，但需要验证多行、空值与无匹配行时的语义。",
                NodeId = "0"
            };
        }
    }
}
