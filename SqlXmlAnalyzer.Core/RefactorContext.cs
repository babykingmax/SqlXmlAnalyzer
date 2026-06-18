using System;
using System.Collections.Generic;

namespace SqlXmlAnalyzer.Core
{
    public record RefactorChange(string RuleId, string Description, DateTime Timestamp);
    public record RefactorFailure(string RuleId, string ExceptionName, string StackTrace, DateTime Timestamp);

    public class RefactorContext
    {
        private readonly List<RefactorChange> _changes = new();
        private readonly List<RefactorFailure> _failures = new();

        public string OriginalSql { get; }
        public AnalysisReport Analysis { get; }
        public bool IsDryRun { get; }

        // New properties
        public IReadOnlyList<RefactorChange> RefactorChanges => _changes.AsReadOnly();
        public IReadOnlyList<RefactorFailure> RefactorFailures => _failures.AsReadOnly();

        // Backward-compatible properties & fields
        public IList<string> Logs { get; } = new List<string>();
        public IList<string> Warnings { get; } = new List<string>();
        public bool Changed { get; set; }

        public RefactorContext(string originalSql)
            : this(originalSql, new AnalysisReport(new List<Abstractions.IAnalysisIssue>()), false)
        {
        }

        public RefactorContext(string originalSql, AnalysisReport analysis, bool isDryRun)
        {
            OriginalSql = originalSql;
            Analysis = analysis;
            IsDryRun = isDryRun;
        }

        // New methods
        internal void RecordChange(string ruleId, string description)
        {
            Changed = true;
            _changes.Add(new RefactorChange(ruleId, description, DateTime.UtcNow));
            Log($"[{ruleId}] {description}");
        }

        internal void RecordFailure(string ruleId, string exceptionName, string stackTrace)
        {
            _failures.Add(new RefactorFailure(ruleId, exceptionName, stackTrace, DateTime.UtcNow));
            Warn($"[{ruleId}] Failed with {exceptionName}: {stackTrace}");
        }

        // Backward-compatible methods
        public void Log(string message) => Logs.Add(message);
        public void Warn(string message) => Warnings.Add(message);
    }
}
