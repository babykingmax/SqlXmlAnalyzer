using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules;

public class RowEstimateMismatchRule : IPlanAnalyzerRule
{
    public string RuleId => "RULE_004_ESTIMATE_MISMATCH";
    public string Name => "Row Estimate Mismatch (10x+ / 100x+)";
    public string Description => "Compares estimated and actual rows per logical execution.";
    public RuleMetadata Metadata => RuleMetadataCatalog.Get(RuleId, Description) with { Version = "2.0.0" };

    public RuleEvaluation Evaluate(RuleAnalysisContext context) => RowCountRuleEvaluator.Evaluate(context);

    public AnalysisResult? Analyze(XElement relOp, XNamespace ns) => RowCountRuleEvaluator.Analyze(RuleId, relOp, ns);
}
