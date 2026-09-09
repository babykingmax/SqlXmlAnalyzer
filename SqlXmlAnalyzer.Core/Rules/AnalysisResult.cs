namespace SqlXmlAnalyzer.Core.Rules
{
    public class AnalysisResult
    {
        public string RuleId { get; set; } = string.Empty;
        public string Severity { get; set; } = "Warning"; // Info, Warning, Critical
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        /// <summary>Evidence scope for this result; null uses the rule's invocation scope.</summary>
        public RuleScope? ResultScope { get; set; }
        public Models.PlanLocation? Location { get; set; }
        public System.Collections.Generic.IReadOnlyList<Models.SqlObjectReference> Objects { get; set; } = System.Array.Empty<Models.SqlObjectReference>();
        public RuleMetadata? Metadata { get; set; }
        public PlanDiagnostic? Diagnostic { get; init; }
        public RuleRun? Run { get; init; }
    }
}
