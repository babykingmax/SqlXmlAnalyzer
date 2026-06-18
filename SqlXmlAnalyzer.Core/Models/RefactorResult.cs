using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Models
{
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
    }
}
