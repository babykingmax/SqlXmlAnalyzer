using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Analysis
{
    public class SqlPlanAnalysisIssue : IAnalysisIssue
    {
        public string IssueType { get; }
        public string Description { get; }
        public IssueSeverity Severity { get; }
        public string? TableName { get; }
        public string? ColumnName { get; }

        public SqlPlanAnalysisIssue(
            string issueType,
            string description,
            IssueSeverity severity,
            string? tableName = null,
            string? columnName = null)
        {
            IssueType = issueType;
            Description = description;
            Severity = severity;
            TableName = tableName;
            ColumnName = columnName;
        }
    }
}
