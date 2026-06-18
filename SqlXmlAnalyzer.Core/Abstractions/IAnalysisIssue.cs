namespace SqlXmlAnalyzer.Core.Abstractions
{
    public interface IAnalysisIssue
    {
        string IssueType { get; }
        string Description { get; }
        IssueSeverity Severity { get; }
        string? TableName { get; }
        string? ColumnName { get; }
    }
}
