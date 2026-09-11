using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules;

public class ResidualPredOpRule : IPlanAnalyzerRule
{
    public string RuleId => "RULE_034_RESIDUAL_PRED_OP";
    public string Name => "Residual Predicate Read Amplification";
    public string Description => "Detects measured read/output differences on Scan or Seek operators with residual predicates.";
    public RuleMetadata Metadata => RuleMetadataCatalog.Get(RuleId, Description);

    public RuleEvaluation Evaluate(RuleAnalysisContext context) => RowCountRuleEvaluator.Evaluate(context);

    public AnalysisResult? Analyze(XElement relOp, XNamespace ns) => RowCountRuleEvaluator.Analyze(RuleId, relOp, ns);
}
