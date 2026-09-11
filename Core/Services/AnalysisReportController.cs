using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;

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
        private readonly MermaidDiagramService _mermaidDiagramService;

        public AnalysisReportController(
            MermaidDiagramService? mermaidDiagramService = null)
        {
            _mermaidDiagramService = mermaidDiagramService
                ?? new MermaidDiagramService();
        }

        public HtmlAnalysisReport BuildDeadlockHtmlReport(
            XDocument document,
            string filePath,
            string selectedDetailText,
            DeadlockAnalysisOutput? completedAnalysis = null)
        {
            var analysis = completedAnalysis ?? new DeadlockAnalysisService().Analyze(document);
            var graph = analysis.Graph;
            string mermaid = analysis.Mermaid;
            string summaryText =
                $"Deadlock file: {Path.GetFileName(filePath)}{Environment.NewLine}" +
                $"Victim process: {string.Join(", ", graph.VictimProcessIds.Order(StringComparer.Ordinal))}{Environment.NewLine}" +
                $"SPIDs: {string.Join(", ", graph.Processes.Select(process => process.Spid).Distinct())}{Environment.NewLine}" +
                graph.CycleAnalysis.Summary + Environment.NewLine + string.Join(Environment.NewLine, analysis.Warnings);

            List<HtmlReportItem> reportItems = analysis.Patterns
                .Select(pattern => new HtmlReportItem(
                    pattern.TypeName,
                    pattern.Description + Environment.NewLine + DeadlockDiagnosticFormatter.FormatEvidence(pattern),
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
            XNamespace showplanNamespace,
            PlanDiagnosticReport? diagnostics = null)
        {
            string mermaid = _mermaidDiagramService.BuildPlanDiagram(
                document,
                showplanNamespace);
            if (diagnostics != null && PlanIdentityAdapter.GetDocument(document) is { } model
                && diagnostics.DocumentId != model.Envelope.DocumentId)
                throw new InvalidDataException("RULE_REPORT_SOURCE_MISMATCH: 诊断记录不属于当前文档版本。");
            diagnostics ??= PlanDiagnosticAnalyzer.AnalyzeDetailed(document, showplanNamespace);
            List<HtmlReportItem> reportItems = diagnostics.ToLegacyResults()
                .Select(result => new HtmlReportItem(
                    result.Title,
                    result.Diagnostic is { } diagnostic ? DiagnosticTextFormatter.FormatDiagnostic(diagnostic) : result.Message,
                    string.Empty,
                    string.Empty,
                    result.Severity))
                .ToList();
            reportItems.AddRange(diagnostics.Runs.Where(r => r.Status == RuleRunStatus.Skipped)
                .Select(r => new HtmlReportItem($"Skipped: {r.RuleId}", DiagnosticTextFormatter.FormatRun(r),
                    string.Empty, string.Empty, "Info")));

            if (reportItems.Count == 0)
            {
                reportItems.Add(new HtmlReportItem(
                    "No diagnostic rule matched",
                    "已执行规则未命中诊断；这不构成计划完全健康的证明。",
                    string.Empty,
                    string.Empty,
                    "Info"));
            }

            string summaryText = $"Execution plan file: {Path.GetFileName(filePath)}{Environment.NewLine}";
            summaryText += DiagnosticTextFormatter.Summary(diagnostics) + Environment.NewLine;
            if (diagnostics.HasFailures) summaryText += "诊断未完成，不能据此判断计划健康。" + Environment.NewLine;
            else if (diagnostics.HasMissingEvidence) summaryText += "部分规则缺少证据，详见 Skipped 记录。" + Environment.NewLine;
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
                    if (!string.IsNullOrEmpty(pattern.RuleId)) builder.AppendLine(DeadlockDiagnosticFormatter.FormatEvidence(pattern));
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
