using System;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    [Obsolete("Use SqlXmlAnalyzer.Core.RefactorContext instead.")]
    public class RefactorContext : SqlXmlAnalyzer.Core.RefactorContext
    {
        public RefactorContext(string originalSql) : base(originalSql) { }
        public RefactorContext(string originalSql, SqlXmlAnalyzer.Core.AnalysisReport analysis, bool isDryRun) : base(originalSql, analysis, isDryRun) { }
    }
}
