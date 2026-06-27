using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum HtmlReportActionStatus
    {
        Ready,
        MissingDocument,
        UnsupportedTab
    }

    public sealed record HtmlReportActionResult(
        HtmlReportActionStatus Status,
        HtmlAnalysisReport? Report,
        string UserMessage);

    public sealed class HtmlReportActionService
    {
        private readonly AnalysisReportController _analysisReportController;

        public HtmlReportActionService(
            AnalysisReportController? analysisReportController = null)
        {
            _analysisReportController = analysisReportController
                ?? new AnalysisReportController();
        }

        public HtmlReportActionResult BuildReport(
            int selectedTabIndex,
            XDocument? deadlockDocument,
            string? deadlockFilePath,
            string deadlockDetailText,
            XDocument? planDocument,
            string? planFilePath,
            XNamespace showplanNamespace)
        {
            ArgumentNullException.ThrowIfNull(showplanNamespace);

            if (selectedTabIndex == 0)
            {
                if (deadlockDocument == null || string.IsNullOrEmpty(deadlockFilePath))
                {
                    return Missing("Please open and analyze a deadlock XML file first.");
                }

                return new HtmlReportActionResult(
                    HtmlReportActionStatus.Ready,
                    _analysisReportController.BuildDeadlockHtmlReport(
                        deadlockDocument,
                        deadlockFilePath,
                        deadlockDetailText),
                    "");
            }

            if (selectedTabIndex == 1)
            {
                if (planDocument == null || string.IsNullOrEmpty(planFilePath))
                {
                    return Missing("Please open and analyze an execution plan file first.");
                }

                return new HtmlReportActionResult(
                    HtmlReportActionStatus.Ready,
                    _analysisReportController.BuildPlanHtmlReport(
                        planDocument,
                        planFilePath,
                        showplanNamespace),
                    "");
            }

            return new HtmlReportActionResult(
                HtmlReportActionStatus.UnsupportedTab,
                null,
                "There is no selected analysis tab.");
        }

        private static HtmlReportActionResult Missing(string message)
        {
            return new HtmlReportActionResult(
                HtmlReportActionStatus.MissingDocument,
                null,
                message);
        }
    }
}
