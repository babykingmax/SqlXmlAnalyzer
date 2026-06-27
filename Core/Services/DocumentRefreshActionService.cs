namespace SqlXmlAnalyzer.Core.Services
{
    public enum DocumentRefreshActionStatus
    {
        Ready,
        MissingFile
    }

    public sealed record DocumentRefreshActionResult(
        DocumentRefreshActionStatus Status,
        string FilePath,
        string UserMessage);

    public sealed class DocumentRefreshActionService
    {
        public DocumentRefreshActionResult BuildDeadlockRefresh(string? currentFilePath)
        {
            return BuildRefresh(
                currentFilePath,
                "没有已加载的死锁文件，无法刷新。");
        }

        public DocumentRefreshActionResult BuildPlanRefresh(string? currentFilePath)
        {
            return BuildRefresh(
                currentFilePath,
                "没有已加载的执行计划文件，无法刷新。");
        }

        private static DocumentRefreshActionResult BuildRefresh(
            string? currentFilePath,
            string missingMessage)
        {
            return string.IsNullOrEmpty(currentFilePath)
                ? new DocumentRefreshActionResult(
                    DocumentRefreshActionStatus.MissingFile,
                    "",
                    missingMessage)
                : new DocumentRefreshActionResult(
                    DocumentRefreshActionStatus.Ready,
                    currentFilePath,
                    "");
        }
    }
}
