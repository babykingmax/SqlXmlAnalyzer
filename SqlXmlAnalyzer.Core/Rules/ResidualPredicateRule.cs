using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules;

public class ResidualPredicateRule : IPlanAnalyzerRule
{
    public string RuleId => "RULE_006_RESIDUAL_PREDICATE";
    public string Name => "Scan or Seek with Residual Predicate";
    public string Description => "Reports local residual predicates and measured read amplification when available.";
    public RuleMetadata Metadata => RuleMetadataCatalog.Get(RuleId, Description);

    public RuleEvaluation Evaluate(RuleAnalysisContext context) => RowCountRuleEvaluator.Evaluate(context);

    public AnalysisResult? Analyze(XElement relOp, XNamespace ns) => RowCountRuleEvaluator.Analyze(RuleId, relOp, ns);
}
