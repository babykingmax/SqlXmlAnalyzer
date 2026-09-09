using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules;

public class CardinalityErrorRule : IPlanAnalyzerRule
{
    public string RuleId => "RULE_030_CARDINALITY_ERROR";
    public string Name => "Cardinality Estimation Deviation Detection";
    public string Description => "Detects large per-execution deviations without asserting a root cause.";
    public RuleMetadata Metadata => RuleMetadataCatalog.Get(RuleId, Description) with { Version = "2.0.0" };

    public RuleEvaluation Evaluate(RuleAnalysisContext context) => RowCountRuleEvaluator.Evaluate(context);

    public AnalysisResult? Analyze(XElement relOp, XNamespace ns) => RowCountRuleEvaluator.Analyze(RuleId, relOp, ns);
}
