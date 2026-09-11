using System;
using System.Collections.Generic;

namespace SqlXmlAnalyzer.Application.Models
{
    public class RefactorReportDto
    {
        public string Privacy => Core.Privacy.OutputPrivacy.RawNotice;
        public bool IsSuccess { get; set; }
        public bool IsDryRun { get; set; }
        public bool HasChanges { get; set; }
        public bool SourceWritten { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string ProposalSchemaVersion => "1";
        public bool CanApply => false;
        public string? SourceHash { get; set; }
        public object? Review { get; set; }

        // Metadata
        public double TimeElapsedMs { get; set; }
        public int OriginalSqlLength { get; set; }
        public int RefactoredSqlLength { get; set; }
        public int PassesCount { get; set; }

        public List<ChangeDto> Changes { get; set; } = new();
        public List<FailureDto> Failures { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<Core.Models.RefactorSafetySkip> SafetySkips { get; set; } = new();
        public Core.Diagnostics.UnexpectedErrorReport? Diagnostic { get; set; }
        public Core.Rules.PlanDiagnosticReport? Diagnostics { get; set; }
        public List<ParseErrorDto>? ParseErrors { get; set; }

        public string? OriginalSql { get; set; }
        public string? RefactoredSql { get; set; }
    }

    public class ChangeDto
    {
        public string RuleId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class FailureDto
    {
        public string RuleId { get; set; } = string.Empty;
        public string ExceptionName { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class ParseErrorDto
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
