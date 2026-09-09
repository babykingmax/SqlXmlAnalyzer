using System.Collections.Generic;
using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Core
{
    public record AnalysisReport(IReadOnlyList<IAnalysisIssue> Issues)
    {
        public Services.InputStatus InputStatus { get; init; } = Services.InputStatus.Success;
        public string? InputErrorCode { get; init; }
        public string? InputErrorMessage { get; init; }
        public Rules.PlanDiagnosticReport? Diagnostics { get; init; }
        public string? AnalysisErrorCode => Diagnostics?.HasFailures == true ? "RULE_EXECUTION_FAILED" : null;
        public string? AnalysisErrorMessage => Diagnostics?.HasFailures == true ? Rules.DiagnosticTextFormatter.Format(Diagnostics) : null;
        public bool IsSuccess => InputStatus == Services.InputStatus.Success && Diagnostics?.HasFailures != true;
        public Services.DocumentEnvelope? InputEnvelope { get; init; }
        public Models.PlanDocument? Plan { get; init; }
        public Services.DocumentCapabilities Capabilities { get; init; }
        public IReadOnlyList<Services.InputDiagnostic> InputDiagnostics { get; init; } = System.Array.Empty<Services.InputDiagnostic>();
    }
}
