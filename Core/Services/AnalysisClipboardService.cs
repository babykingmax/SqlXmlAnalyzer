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
                    "=== SQL Server 死锁诊断报告 ===",
                    "当前没有死锁诊断结果可复制！"),
                1 => Build(
                    planDiagnostics,
                    "=== SQL Server 执行计划诊断报告 ===",
                    "当前没有执行计划诊断结果可复制！"),
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
                    "当前没有重构后的 SQL 可复制！");
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
