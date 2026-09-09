namespace SqlXmlAnalyzer.Core.Abstractions
{
    public interface IAnalysisIssue
    {
        string IssueType { get; }
        string Description { get; }
        IssueSeverity Severity { get; }
        string? TableName { get; }
        string? ColumnName { get; }
        Models.PlanLocation? Location => null;
        Rules.PlanDiagnostic? Diagnostic => null;
        Rules.RuleRun? Run => null;
        System.Collections.Generic.IReadOnlyList<Models.SqlObjectReference> Objects => System.Array.Empty<Models.SqlObjectReference>();
    }
}
