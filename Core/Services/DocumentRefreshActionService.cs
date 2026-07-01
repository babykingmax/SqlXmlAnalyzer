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
                "No loaded deadlock file is available to refresh.");
        }

        public DocumentRefreshActionResult BuildPlanRefresh(string? currentFilePath)
        {
            return BuildRefresh(
                currentFilePath,
                "No loaded execution plan file is available to refresh.");
        }

        private static DocumentRefreshActionResult BuildRefresh(
            string? currentFilePath,
            string missingMessage)
        {
            return string.IsNullOrEmpty(currentFilePath)
                ? new DocumentRefreshActionResult(
                    DocumentRefreshActionStatus.MissingFile,
                    string.Empty,
                    missingMessage)
                : new DocumentRefreshActionResult(
                    DocumentRefreshActionStatus.Ready,
                    currentFilePath,
                    string.Empty);
        }
    }
}
