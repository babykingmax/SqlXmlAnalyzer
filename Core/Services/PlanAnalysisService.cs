using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanAnalysisOutput(
        string Mermaid,
        string QueryText,
        string DocumentText,
        string WarningsText,
        List<MissingIndexSuggestion> MissingIndexes,
        string RefactoredSql)
    {
        public PlanDocument? Plan { get; init; }
        public Rules.PlanDiagnosticReport? Diagnostics { get; init; }
        public RewriteReview? RewriteReview { get; init; }
        public string RefactoringNotices { get; init; } = "";
    }

    public sealed class PlanAnalysisService
    {
        private readonly ApplicationOrchestrator _orchestrator;
        private readonly IFileHandler _fileHandler;
        private readonly TemporaryFileManager _temporaryFileManager;
        private readonly IUnexpectedErrorReporter _unexpectedErrors;
        private readonly Func<Rules.RuleEngine>? _diagnosticEngineFactory;

        public PlanAnalysisService(
            ApplicationOrchestrator orchestrator,
            IFileHandler fileHandler,
            TemporaryFileManager temporaryFileManager,
            IUnexpectedErrorReporter? unexpectedErrors = null, Func<Rules.RuleEngine>? diagnosticEngineFactory = null)
        {
            _orchestrator = orchestrator;
            _fileHandler = fileHandler;
            _temporaryFileManager = temporaryFileManager;
            _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
            _diagnosticEngineFactory = diagnosticEngineFactory;
        }

        public PlanAnalysisOutput Analyze(
            XDocument document,
            XNamespace showplanNamespace,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = PlanIdentityAdapter.GetDocument(document, cancellationToken);
            string mermaid = ExecutionPlanVisualizer.GenerateMermaidPlan(
                document,
                showplanNamespace);
            string queryText = document
                .Descendants(showplanNamespace + "StmtSimple")
                .FirstOrDefault()?
                .Attribute("StatementText")?
                .Value ?? "未能提取语句";
            string documentText = document.ToString();
            if (plan?.Statements.Count > 1)
                queryText = string.Join(Environment.NewLine + Environment.NewLine, plan.Statements.Select(statement =>
                    $"-- Batch {statement.Key.Batch.BatchOrdinal} / Statement {statement.Key.StatementOrdinal} ({statement.Kind})" +
                    Environment.NewLine + (statement.Text ?? "-- 此语句未提供 StatementText")));
            var diagnostics = _diagnosticEngineFactory?.Invoke().AnalyzePlanDetailed(document, showplanNamespace, plan, cancellationToken: cancellationToken)
                ?? PlanDiagnosticAnalyzer.AnalyzeDetailed(document, showplanNamespace, identities: plan, cancellationToken: cancellationToken, unexpectedErrors: _unexpectedErrors);
            string warningsText = Rules.DiagnosticTextFormatter.Format(diagnostics);
            List<MissingIndexSuggestion> missingIndexes =
                PlanDiagnosticAnalyzer.ExtractMissingIndexes(
                    document,
                    showplanNamespace);
            cancellationToken.ThrowIfCancellationRequested();

            RefactorPresentation refactoring = diagnostics.HasFailures
                ? new(queryText, "规则诊断未完成，自动重构未执行；请查看 Failed 运行记录。")
                : plan?.Statements.Count > 1
                ? new(queryText, "多语句计划已保留所有语句身份；自动重构需要明确选定单条语句，本次未生成跨语句改写。")
                : RefactorSql(
                queryText,
                filePath,
                cancellationToken);
            return new PlanAnalysisOutput(
                mermaid,
                queryText,
                documentText,
                warningsText + Environment.NewLine + refactoring.Notices,
                missingIndexes,
                refactoring.Sql) { Plan = plan, Diagnostics = diagnostics, RewriteReview = refactoring.Review,
                    RefactoringNotices = refactoring.Notices };
        }

        private RefactorPresentation RefactorSql(
            string queryText,
            string planFilePath,
            CancellationToken cancellationToken)
        {
            if (queryText == "未能提取语句" || string.IsNullOrWhiteSpace(queryText))
            {
                return new(queryText, string.Empty);
            }

            try
            {
                string temporarySqlPath =
                    _temporaryFileManager.CreatePath("Refactor", ".sql");
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _fileHandler.WriteAllText(temporarySqlPath, queryText);
                    var options = new RefactorOptions { MaxPasses = 5 };
                    string? planPath = _fileHandler.Exists(planFilePath)
                        ? planFilePath
                        : null;
                    var result = _orchestrator.Execute(
                        temporarySqlPath,
                        planPath: planPath,
                        isDryRun: true,
                        options: options,
                        cancellationToken: cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    return RefactorPresentation.FromResult(result, queryText);
                }
                finally
                {
                    _temporaryFileManager.Delete(temporarySqlPath);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string detail = ExceptionPolicy.Describe(ex, "PlanAnalysisService.RefactorSql", _unexpectedErrors);
                return new(queryText, $"T-SQL 重构失败，已保留原 SQL：{detail}");
            }
        }
    }
}
