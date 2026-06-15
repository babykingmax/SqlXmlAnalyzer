using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public class RefactorResult
    {
        public string OriginalSql { get; }
        public string RefactoredSql { get; }
        public bool HasChanges { get; }
        public IList<ParseError> ParseErrors { get; }
        public IList<string> Logs { get; }
        public IList<string> Warnings { get; }

        public RefactorResult(string originalSql, string refactoredSql, bool hasChanges, IList<ParseError> parseErrors, IList<string> logs, IList<string> warnings)
        {
            OriginalSql = originalSql;
            RefactoredSql = refactoredSql;
            HasChanges = hasChanges;
            ParseErrors = parseErrors;
            Logs = logs;
            Warnings = warnings;
        }
    }
}
