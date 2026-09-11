using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public interface IPlanAnalyzerRule
    {
        string RuleId { get; }
        string Name { get; }
        string Description { get; }
        RuleMetadata Metadata => RuleMetadataCatalog.Get(RuleId, Description);

        /// <summary>New diagnostic protocol; existing rules are adapted without changing IDs or thresholds.</summary>
        RuleEvaluation Evaluate(RuleAnalysisContext context) => LegacyDiagnosticAdapter.Evaluate(this, context);

        /// <summary>
        /// Analyzes an XML RelOp node and returns a result if the rule is triggered.
        /// </summary>
        AnalysisResult? Analyze(XElement relOp, XNamespace ns) =>
            throw new NotSupportedException("This rule requires the diagnostic protocol entry point.");
    }
}
