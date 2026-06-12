namespace SqlXmlAnalyzer.Core.Rules
{
    public class AnalysisResult
    {
        public string RuleId { get; set; } = string.Empty;
        public string Severity { get; set; } = "Warning"; // Info, Warning, Critical
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
    }
}
