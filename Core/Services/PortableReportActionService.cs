using System.Collections.Generic;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum PortableReportActionStatus
    {
        Ready,
        MissingContent,
        UnsupportedTab
    }

    public sealed record PortableReportActionResult(
        PortableReportActionStatus Status,
        PortableAnalysisReport? Report,
        bool IncludeDeadlockDiagram,
        string UserMessage);

    public sealed class PortableReportActionService
    {
        private readonly AnalysisReportController _analysisReportController;

        public PortableReportActionService(
            AnalysisReportController? analysisReportController = null)
        {
            _analysisReportController = analysisReportController
                ?? new AnalysisReportController();
        }

        public PortableReportActionResult BuildReport(
            int selectedTabIndex,
            string? deadlockFilePath,
            IEnumerable<DeadlockPattern>? deadlockPatterns,
            string deadlockDetailText,
            string? planFilePath,
            string planDiagnosticsText,
            string extension)
        {
            if (selectedTabIndex == 0)
            {
                if (deadlockFilePath == null)
                {
                    return Missing("There is no loaded deadlock document to export.");
                }

                PortableAnalysisReport report =
                    _analysisReportController.BuildDeadlockPortableReport(
                        deadlockFilePath,
                        deadlockPatterns,
                        deadlockDetailText,
                        extension);

                return new PortableReportActionResult(
                    PortableReportActionStatus.Ready,
                    report,
                    report.IncludeDeadlockDiagram,
                    "");
            }

            if (selectedTabIndex == 1)
            {
                if (string.IsNullOrWhiteSpace(planDiagnosticsText) || planFilePath == null)
                {
                    return Missing("There are no execution plan diagnostics to export.");
                }

                return new PortableReportActionResult(
                    PortableReportActionStatus.Ready,
                    _analysisReportController.BuildPlanPortableReport(
                        planFilePath,
                        planDiagnosticsText,
                        extension),
                    IncludeDeadlockDiagram: false,
                    "");
            }

            return new PortableReportActionResult(
                PortableReportActionStatus.UnsupportedTab,
                null,
                IncludeDeadlockDiagram: false,
                "");
        }

        private static PortableReportActionResult Missing(string userMessage)
        {
            return new PortableReportActionResult(
                PortableReportActionStatus.MissingContent,
                null,
                IncludeDeadlockDiagram: false,
                userMessage);
        }
    }
}
