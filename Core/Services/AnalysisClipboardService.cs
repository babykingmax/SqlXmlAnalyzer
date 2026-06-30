namespace SqlXmlAnalyzer.Core.Services
{
    public enum AnalysisClipboardStatus
    {
        Ready,
        Empty,
        UnsupportedTab
    }

    public sealed record AnalysisClipboardResult(
        AnalysisClipboardStatus Status,
        string Text,
        string? UserMessage);

    public sealed class AnalysisClipboardService
    {
        public AnalysisClipboardResult BuildForTab(
            int selectedTabIndex,
            string? deadlockDiagnostics,
            string? planDiagnostics)
        {
            return selectedTabIndex switch
            {
                0 => Build(
                    deadlockDiagnostics,
                    "=== SQL Server Deadlock Diagnostic Report ===",
                    "There are no deadlock diagnostics to copy."),
                1 => Build(
                    planDiagnostics,
                    "=== SQL Server Execution Plan Diagnostic Report ===",
                    "There are no execution plan diagnostics to copy."),
                _ => new AnalysisClipboardResult(
                    AnalysisClipboardStatus.UnsupportedTab,
                    string.Empty,
                    null)
            };
        }

        public AnalysisClipboardResult BuildRefactoredSql(string? refactoredSql)
        {
            if (string.IsNullOrEmpty(refactoredSql))
            {
                return new AnalysisClipboardResult(
                    AnalysisClipboardStatus.Empty,
                    string.Empty,
                    "There is no refactored SQL to copy.");
            }

            return new AnalysisClipboardResult(
                AnalysisClipboardStatus.Ready,
                refactoredSql,
                null);
        }

        private static AnalysisClipboardResult Build(
            string? diagnostics,
            string header,
            string emptyMessage)
        {
            if (string.IsNullOrWhiteSpace(diagnostics))
            {
                return new AnalysisClipboardResult(
                    AnalysisClipboardStatus.Empty,
                    string.Empty,
                    emptyMessage);
            }

            return new AnalysisClipboardResult(
                AnalysisClipboardStatus.Ready,
                $"{header}\r\n\r\n{diagnostics}",
                null);
        }
    }
}
