namespace SqlXmlAnalyzer.Core.Models;

/// <summary>A detected rewrite which is unavailable until its semantic prerequisites can be proved.</summary>
public sealed record RefactorSafetySkip(string RuleId, string ReasonCode, string Reason, string RequiredEvidence);
