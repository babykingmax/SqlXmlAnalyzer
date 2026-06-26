using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record HtmlAnalysisReport(
        string OriginalFilePath,
        string AnalysisType,
        string SummaryText,
        string MermaidCode,
        IReadOnlyList<HtmlReportSection> Sections,
        string DefaultFileName,
        IReadOnlyList<MissingIndexSuggestion> MissingIndexes);

    public sealed record PortableAnalysisReport(
        string Title,
        string Content,
        string DefaultFileName,
        bool IncludeDeadlockDiagram);

    public sealed class AnalysisReportController
    {
        public HtmlAnalysisReport BuildDeadlockHtmlReport(
            XDocument document,
            string filePath,
            string selectedDetailText)
        {
            var parseResult = DeadlockXmlParser.TryParseDeadlockXml(document);
            if (!parseResult.IsSuccess || parseResult.Value == null)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, parseResult.Errors));
            }

            var parsed = parseResult.Value;
            var graph = DeadlockGraphBuilder.Build(
                parsed.Processes,
                parsed.Resources,
                parsed.VictimId);
            string mermaid = DeadlockGraphBuilder.GenerateMermaid(graph, true);
            string summaryText =
                $"Deadlock file: {Path.GetFileName(filePath)}{Environment.NewLine}" +
                $"Victim process: {parsed.VictimId}{Environment.NewLine}" +
                $"SPIDs: {string.Join(", ", parsed.Processes.Select(process => process.Spid).Distinct())}";

            List<HtmlReportItem> reportItems = DeadlockPatternAnalyzer
                .IdentifyPatterns(graph, document)
                .Select(pattern => new HtmlReportItem(
                    pattern.TypeName,
                    pattern.Description,
                    pattern.LikelyCause,
                    pattern.Recommendation,
                    pattern.Severity))
                .ToList();

            if (!string.IsNullOrWhiteSpace(selectedDetailText))
            {
                reportItems.Add(new HtmlReportItem(
                    "Selected item analysis",
                    selectedDetailText,
                    string.Empty,
                    string.Empty,
                    "Info"));
            }

            return new HtmlAnalysisReport(
                filePath,
                "Deadlock",
                summaryText,
                mermaid,
                CreateDiagnosticSections(reportItems),
                $"DeadlockReport_{Path.GetFileNameWithoutExtension(filePath)}.html",
                Array.Empty<MissingIndexSuggestion>());
        }

        public HtmlAnalysisReport BuildPlanHtmlReport(
            XDocument document,
            string filePath,
            XNamespace showplanNamespace)
        {
            string mermaid = ExecutionPlanVisualizer.GenerateMermaidPlan(
                document,
                showplanNamespace);
            List<HtmlReportItem> reportItems = PlanDiagnosticAnalyzer
                .AnalyzePlan(document, showplanNamespace)
                .Select(result => new HtmlReportItem(
                    result.Title,
                    result.Message,
                    string.Empty,
                    string.Empty,
                    result.Severity))
                .ToList();

            if (reportItems.Count == 0)
            {
                reportItems.Add(new HtmlReportItem(
                    "No diagnostic rule matched",
                    "The current execution plan did not match any enabled diagnostic rule.",
                    string.Empty,
                    string.Empty,
                    "Info"));
            }

            string summaryText = $"Execution plan file: {Path.GetFileName(filePath)}{Environment.NewLine}";
            XElement? queryPlan = document.Descendants(showplanNamespace + "QueryPlan").FirstOrDefault();
            if (queryPlan != null)
            {
                string totalCost = queryPlan.Attribute("EstimatedTotalSubtreeCost")?.Value ?? "N/A";
                summaryText += $"Estimated total cost: {totalCost}{Environment.NewLine}";
            }

            return new HtmlAnalysisReport(
                filePath,
                "ExecutionPlan",
                summaryText,
                mermaid,
                CreateDiagnosticSections(reportItems),
                $"ExecutionPlanReport_{Path.GetFileNameWithoutExtension(filePath)}.html",
                PlanDiagnosticAnalyzer.ExtractMissingIndexes(document, showplanNamespace));
        }

        public PortableAnalysisReport BuildDeadlockPortableReport(
            string filePath,
            IEnumerable<DeadlockPattern>? patterns,
            string selectedDetailText,
            string extension)
        {
            var builder = new StringBuilder();
            builder.AppendLine("=== Deadlock Pattern Diagnostics ===");

            bool hasPatterns = false;
            if (patterns != null)
            {
                foreach (DeadlockPattern pattern in patterns)
                {
                    builder.AppendLine(pattern.TypeName);
                    builder.AppendLine($"Description: {pattern.Description}");
                    builder.AppendLine($"Likely cause: {pattern.LikelyCause}");
                    builder.AppendLine($"Recommendation: {pattern.Recommendation}");
                    builder.AppendLine();
                    hasPatterns = true;
                }
            }

            if (!hasPatterns)
            {
                builder.AppendLine("No known deadlock pattern was detected.");
                builder.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(selectedDetailText))
            {
                builder.AppendLine("=== Selected Item Analysis ===");
                builder.AppendLine(RemoveUnsupportedReportGlyphs(selectedDetailText));
            }

            return new PortableAnalysisReport(
                "SQL Server Deadlock Diagnostic Report",
                builder.ToString(),
                $"DeadlockReport_{Path.GetFileNameWithoutExtension(filePath)}.{extension}",
                IncludeDeadlockDiagram: true);
        }

        public PortableAnalysisReport BuildPlanPortableReport(
            string filePath,
            string diagnosticsText,
            string extension)
        {
            return new PortableAnalysisReport(
                "SQL Server Execution Plan Diagnostic Report",
                diagnosticsText,
                $"PlanReport_{Path.GetFileNameWithoutExtension(filePath)}.{extension}",
                IncludeDeadlockDiagram: false);
        }

        private static IReadOnlyList<HtmlReportSection> CreateDiagnosticSections(
            IReadOnlyList<HtmlReportItem> reportItems)
        {
            return new[]
            {
                new HtmlReportSection(
                    "Detailed diagnostics and recommendations",
                    reportItems)
            };
        }

        private static string RemoveUnsupportedReportGlyphs(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (char.IsSurrogate(character) ||
                    character == '\uFE0F' ||
                    category == UnicodeCategory.OtherSymbol)
                {
                    continue;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }
    }
}
