using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Models
{
    public enum RefactorOutcome { Failed, NoChanges, CandidateGenerated, Applied }

    public record RefactorResult(
        string OutputSql,
        bool IsSuccess,
        IReadOnlyList<string> Errors,
        RefactorContext Context,
        IReadOnlyList<ParseError>? ParseErrors = null
    )
    {
        public double TimeElapsedMs { get; init; }
        public int PassesCount { get; init; }
        public Diagnostics.UnexpectedErrorReport? Diagnostic { get; init; }
        /// <summary>Canonical review result. OutputSql remains the full candidate for legacy preview consumers.</summary>
        public RewriteReview? Review { get; init; }
        /// <summary>Set by the application only after file writeback is confirmed.</summary>
        public bool SourceWritten { get; init; }
        public RefactorOutcome Outcome => !IsSuccess ? RefactorOutcome.Failed : SourceWritten
            ? RefactorOutcome.Applied : Context.Changed ? RefactorOutcome.CandidateGenerated : RefactorOutcome.NoChanges;
    }
}
