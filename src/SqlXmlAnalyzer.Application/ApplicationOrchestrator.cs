using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Application
{
    public class ApplicationOrchestrator
    {
        private readonly IAnalysisEngine _analysisEngine;
        private readonly IRefactoringEngine _refactoringEngine;
        private readonly IFileHandler _fileHandler;
        private readonly IResultReporter _reporter;
        private readonly ILogger<ApplicationOrchestrator> _logger;
        private readonly ISqlWritebackService _writebackService;
        private readonly IUnexpectedErrorReporter _unexpectedErrors;
        private readonly InputRecognitionService _inputRecognition;

        public ApplicationOrchestrator(
            IAnalysisEngine analysisEngine,
            IRefactoringEngine refactoringEngine,
            IFileHandler fileHandler,
            IResultReporter reporter,
            ILogger<ApplicationOrchestrator> logger,
            ISqlWritebackService? writebackService = null,
            IUnexpectedErrorReporter? unexpectedErrors = null,
            InputRecognitionService? inputRecognition = null)
        {
            _analysisEngine = analysisEngine;
            _refactoringEngine = refactoringEngine;
            _fileHandler = fileHandler;
            _reporter = reporter;
            _logger = logger;
            _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
            _inputRecognition = inputRecognition ?? new InputRecognitionService(_unexpectedErrors, openRead: _fileHandler.OpenRead);
            _writebackService = writebackService ?? new SqlWritebackService(new PhysicalSqlWritebackFileSystem(), unexpectedErrors: _unexpectedErrors);
        }

        public OrchestratorResult PrepareReview(string sql, InputRecognitionResult input, Core.Models.PlanDocument plan,
            Core.Rules.PlanDiagnosticReport diagnostics, System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report = Analysis.SqlXmlAnalysisEngine.FromDiagnostics(input, plan, diagnostics);
            if (!report.IsSuccess || diagnostics.HasFailures)
                throw new InvalidDataException("诊断未完成，不能生成改写提案。");
            var result = _refactoringEngine.Run(sql, report, new RefactorOptions { MaxPasses = 5 }, true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var warnings = new List<string>(result.Context.Warnings);
            if (result.IsSuccess && (result.Errors.Count > 0 || result.ParseErrors?.Count > 0 || result.Context.RefactorFailures.Count > 0))
                result = result with { IsSuccess = false, OutputSql = sql, Review = null };
            result = result with { SourceWritten = false };
            if (result.IsSuccess && (result.Context.Changed || !string.Equals(sql, result.OutputSql, StringComparison.Ordinal)))
            {
                warnings.Add("已生成可审核提案，尚未应用；语义等价性未证明。选择只改变审核预览，不写回源文件。");
                if (result.Review == null) warnings.Add("MissingProposalContract: 改写引擎未提供可审核提案，禁止应用。");
            }
            foreach (var warning in warnings) result.Context.Warn(warning);
            Logger.Debug("IMP26 rewrite preparation reused the existing diagnostic snapshot without file I/O.");
            return new OrchestratorResult(result, result.IsSuccess, null, null, warnings);
        }

        public OrchestratorResult Execute(
            string sqlPath,
            string? planPath = null,
            bool isDryRun = false,
            RefactorOptions? options = null,
            string? outputPath = null,
            System.Threading.CancellationToken cancellationToken = default,
            InputRecognitionResult? planInput = null,
            IReadOnlyList<string>? additionalInputPaths = null)
        {
            var warnings = new List<string>();
            string?[] inputPaths = new string?[] { sqlPath, planPath }.Concat(additionalInputPaths ?? []).ToArray();

            _logger.LogDebug("OrchestrationStarted: HasPlan={HasPlan}, DryRun={DryRun}", planPath != null || planInput != null, isDryRun);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SqlReportPathGuard.ValidateInputs(inputPaths, outputPath);
                if (!_fileHandler.Exists(sqlPath))
                {
                    throw new FileNotFoundException($"SQL file not found: {sqlPath}", sqlPath);
                }

                SqlFileSnapshot? snapshot = isDryRun ? null : _writebackService.ReadSnapshot(sqlPath, cancellationToken);
                var sql = snapshot?.Text ?? _fileHandler.ReadAllText(sqlPath);
                var report = new AnalysisReport(ImmutableList<IAnalysisIssue>.Empty);

                if (planPath != null || planInput != null)
                {
                    // Desktop reanalysis supplies the already-read document and its
                    // provenance. Never reopen a possibly changed path in that case.
                    InputRecognitionResult input = (planInput ?? _inputRecognition.Load(planPath!, cancellationToken)).ForExecutionPlan();
                    if (!input.IsSuccess)
                        return new OrchestratorResult(null, false,
                            $"{input.ErrorCode}: {input.ErrorMessage}", null, warnings);

                    report = _analysisEngine.AnalyzeInput(input, cancellationToken);

                    if (!report.IsSuccess)
                    {
                        // Recognition already recorded the diagnostic (and captured any unknown exception).
                        return new OrchestratorResult(null, false,
                            $"{report.AnalysisErrorCode ?? report.InputErrorCode}: {report.AnalysisErrorMessage ?? report.InputErrorMessage}", null, warnings)
                            { Diagnostics = report.Diagnostics };
                    }

                    foreach (var issue in report.Issues)
                    {
                        if (issue.IssueType == "PARSE_ERROR")
                        {
                            throw new System.Xml.XmlException($"XML Execution Plan parsing failed for '{planPath}'. Details: {issue.Description}");
                        }
                    }
                }

                var opt = options ?? new RefactorOptions();
                cancellationToken.ThrowIfCancellationRequested();
                var result = _refactoringEngine.Run(sql, report, opt, isDryRun, cancellationToken);
                warnings.AddRange(result.Context.Warnings);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.IsSuccess && (result.Errors.Count > 0 || result.ParseErrors?.Count > 0 ||
                    result.Context.RefactorFailures.Count > 0))
                {
                    var errors = new List<string>(result.Errors) { "重构记录了执行错误，禁止写回 SQL。" };
                    result = result with { IsSuccess = false, Errors = errors, OutputSql = sql, Review = null };
                }

                // IMP-18: generation/selection only. IsSuccess and --dry-run=false never authorize Apply.
                result = result with { SourceWritten = false };
                if (result.IsSuccess && (result.Context.Changed || !string.Equals(sql, result.OutputSql, StringComparison.Ordinal)))
                {
                    warnings.Add("已生成可审核提案，尚未应用；语义等价性未证明。选择只改变审核预览，不写回源文件。");
                    if (result.Review == null)
                        warnings.Add("MissingProposalContract: 改写引擎未提供可审核提案，禁止应用。");
                    _logger.LogWarning("RewriteReviewRequired: Candidate generation does not authorize source writeback.");
                }
                if (result.Context != null)
                {
                    foreach (var warning in warnings)
                    {
                        result.Context.Warn(warning);
                    }
                }

                _reporter.Report(result, isDryRun, outputPath, inputPaths, cancellationToken);
                _logger.LogInformation("OrchestrationCompleted: Success={IsSuccess}", result.IsSuccess);
                return new OrchestratorResult(result, result.IsSuccess, null, null, warnings);
            }
            catch (OperationCanceledException ex)
            {
                return new OrchestratorResult(null, false, "操作已取消，未执行 SQL 写回。", ex, warnings);
            }
            catch (System.Text.DecoderFallbackException ex)
            {
                return new OrchestratorResult(null, false,
                    "SQL 文件编码无法安全解码，未写回。请使用 UTF-8 或带 BOM 的 UTF-16/UTF-32 副本后重试。", ex, warnings);
            }
            catch (FileNotFoundException ex)
            {
                var isSqlFile = string.Equals(ex.FileName ?? "", sqlPath, StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("SQL file not found");
                var fileDescription = isSqlFile ? "SQL file" : "File";
                var errMsg = $"[ERROR] {fileDescription} not found: '{ex.FileName ?? sqlPath}'.\nSuggestion: Please check if the file path is correct and ensure the file exists at the specified location.";
                _logger.LogError(ex, errMsg);
                _logger.LogInformation("OrchestrationCompleted: Success=False");
                return new OrchestratorResult(null, false, errMsg, ex, warnings);
            }
            catch (UnauthorizedAccessException ex)
            {
                var errMsg = $"[ERROR] Access denied to file.\nDetails: {ex.Message}\nSuggestion: Please check file/folder permissions and ensure the current user has read/write permissions.";
                _logger.LogError(ex, errMsg);
                _logger.LogInformation("OrchestrationCompleted: Success=False");
                return new OrchestratorResult(null, false, errMsg, ex, warnings);
            }
            catch (IOException ex)
            {
                var errMsg = $"[ERROR] IO Exception while reading or writing files.\nDetails: {ex.Message}\nSuggestion: The file might be locked by another process or there is insufficient disk space.";
                _logger.LogError(ex, errMsg);
                _logger.LogInformation("OrchestrationCompleted: Success=False");
                return new OrchestratorResult(null, false, errMsg, ex, warnings);
            }
            catch (System.Xml.XmlException ex)
            {
                var errMsg = $"[ERROR] Invalid XML execution plan.\nDetails: {ex.Message}\nSuggestion: Please verify the execution plan file is a valid XML document (.sqlplan / .xdl) exported from SQL Server or another compatible database tool.";
                _logger.LogError(ex, errMsg);
                _logger.LogInformation("OrchestrationCompleted: Success=False");
                return new OrchestratorResult(null, false, errMsg, ex, warnings);
            }
            catch (Exception ex)
            {
                var errMsg = $"[ERROR] Orchestration pipeline crashed: {ExceptionPolicy.Describe(ex, "ApplicationOrchestrator", _unexpectedErrors)}";
                _logger.LogCritical(ex, errMsg);
                _logger.LogInformation("OrchestrationCompleted: Success=False");
                return new OrchestratorResult(null, false, errMsg, ex, warnings);
            }
        }
    }
}
