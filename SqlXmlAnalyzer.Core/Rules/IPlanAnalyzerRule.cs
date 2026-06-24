using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public interface IPlanAnalyzerRule
    {
        string RuleId { get; }
        string Name { get; }
        string Description { get; }
        RuleMetadata Metadata => RuleMetadataCatalog.Get(RuleId, Description);

        /// <summary>
        /// Analyzes an XML RelOp node and returns a result if the rule is triggered.
        /// </summary>
        AnalysisResult? Analyze(XElement relOp, XNamespace ns);
    }
}
