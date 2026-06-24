using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanAnalysisOutput(
        string Mermaid,
        string QueryText,
        string DocumentText,
        string WarningsText,
        List<MissingIndexSuggestion> MissingIndexes,
        string RefactoredSql);

    public sealed class PlanAnalysisService
    {
        private readonly ApplicationOrchestrator _orchestrator;
        private readonly IFileHandler _fileHandler;
        private readonly TemporaryFileManager _temporaryFileManager;

        public PlanAnalysisService(
            ApplicationOrchestrator orchestrator,
            IFileHandler fileHandler,
            TemporaryFileManager temporaryFileManager)
        {
            _orchestrator = orchestrator;
            _fileHandler = fileHandler;
            _temporaryFileManager = temporaryFileManager;
        }

        public PlanAnalysisOutput Analyze(
            XDocument document,
            XNamespace showplanNamespace,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string mermaid = ExecutionPlanVisualizer.GenerateMermaidPlan(
                document,
                showplanNamespace);
            string queryText = document
                .Descendants(showplanNamespace + "StmtSimple")
                .FirstOrDefault()?
                .Attribute("StatementText")?
                .Value ?? "未能提取语句";
            string documentText = document.ToString();
            string warningsText = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(
                document,
                showplanNamespace);
            List<MissingIndexSuggestion> missingIndexes =
                PlanDiagnosticAnalyzer.ExtractMissingIndexes(
                    document,
                    showplanNamespace);
            cancellationToken.ThrowIfCancellationRequested();

            string refactoredSql = RefactorSql(
                queryText,
                filePath,
                cancellationToken);
            return new PlanAnalysisOutput(
                mermaid,
                queryText,
                documentText,
                warningsText,
                missingIndexes,
                refactoredSql);
        }

        private string RefactorSql(
            string queryText,
            string planFilePath,
            CancellationToken cancellationToken)
        {
            if (queryText == "未能提取语句" || string.IsNullOrWhiteSpace(queryText))
            {
                return queryText;
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
                        options: options);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!result.IsSuccess || result.Result == null)
                    {
                        return $"/* T-SQL 智能重构失败: {result.ErrorMessage} */\r\n"
                            + queryText;
                    }
                    if (result.Result.Errors.Count == 0)
                    {
                        return result.Result.OutputSql;
                    }

                    var errorText = new System.Text.StringBuilder();
                    errorText.AppendLine("/*");
                    errorText.AppendLine("T-SQL 智能重构失败，解析语法树时发生以下错误：");
                    foreach (string error in result.Result.Errors)
                    {
                        errorText.AppendLine($"- {error}");
                    }
                    errorText.AppendLine("*/");
                    errorText.AppendLine(queryText);
                    return errorText.ToString();
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
                return $"/* T-SQL 智能重构发生意外异常: {ex.Message} */\r\n"
                    + queryText;
            }
        }
    }
}
