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
        public Core.Models.PlanLocation? Location { get; init; }
        public Core.Rules.PlanDiagnostic? Diagnostic { get; init; }
        public Core.Rules.RuleRun? Run { get; init; }
        public System.Collections.Generic.IReadOnlyList<Core.Models.SqlObjectReference> Objects { get; init; } = System.Array.Empty<Core.Models.SqlObjectReference>();

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
