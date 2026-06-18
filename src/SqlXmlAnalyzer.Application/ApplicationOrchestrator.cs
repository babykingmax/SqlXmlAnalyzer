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

namespace SqlXmlAnalyzer.Application
{
    public class ApplicationOrchestrator
    {
        private readonly IAnalysisEngine _analysisEngine;
        private readonly IRefactoringEngine _refactoringEngine;
        private readonly IFileHandler _fileHandler;
        private readonly IResultReporter _reporter;
        private readonly ILogger<ApplicationOrchestrator> _logger;

        public ApplicationOrchestrator(
            IAnalysisEngine analysisEngine,
            IRefactoringEngine refactoringEngine,
            IFileHandler fileHandler,
            IResultReporter reporter,
            ILogger<ApplicationOrchestrator> logger)
        {
            _analysisEngine = analysisEngine;
            _refactoringEngine = refactoringEngine;
            _fileHandler = fileHandler;
            _reporter = reporter;
            _logger = logger;
        }

        public OrchestratorResult Execute(
            string sqlPath, 
            string? planPath = null, 
            bool isDryRun = false, 
            RefactorOptions? options = null,
            string? outputPath = null)
        {
            var warnings = new List<string>();
            _logger.LogInformation("OrchestrationStarted: SQL file: {SqlPath}, Execution Plan: {PlanPath}, DryRun: {DryRun}", sqlPath, planPath, isDryRun);
            try
            {
                if (!_fileHandler.Exists(sqlPath))
                {
                    throw new FileNotFoundException($"SQL file not found: {sqlPath}", sqlPath);
                }

                var sql = _fileHandler.ReadAllText(sqlPath);
                var report = new AnalysisReport(ImmutableList<IAnalysisIssue>.Empty);

                if (planPath != null)
                {
                    if (_fileHandler.Exists(planPath))
                    {
                        var xmlContent = _fileHandler.ReadAllText(planPath);
                        report = _analysisEngine.Analyze(xmlContent);

                        foreach (var issue in report.Issues)
                        {
                            if (issue.IssueType == "PARSE_ERROR")
                            {
                                throw new System.Xml.XmlException($"XML Execution Plan parsing failed for '{planPath}'. Details: {issue.Description}");
                            }
                        }
                    }
                    else
                    {
                        var warningMsg = $"XML plan file not found: {planPath}. Proceeding with empty analysis report.";
                        warnings.Add(warningMsg);
                        _logger.LogWarning(warningMsg);
                    }
                }

                var opt = options ?? new RefactorOptions();
                var result = _refactoringEngine.Run(sql, report, opt, isDryRun);
                
                if (result.IsSuccess && !isDryRun)
                {
                    string backupPath = sqlPath + ".bak";
                    try
                    {
                        _fileHandler.WriteAllText(backupPath, sql);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create backup file: {BackupPath}", backupPath);
                    }
                    _fileHandler.WriteAllText(sqlPath, result.OutputSql);
                }

                if (result.Context != null)
                {
                    foreach (var warning in warnings)
                    {
                        result.Context.Warn(warning);
                    }
                }

                _reporter.Report(result, isDryRun, outputPath);
                _logger.LogInformation("OrchestrationCompleted: Success={IsSuccess}", result.IsSuccess);
                return new OrchestratorResult(result, result.IsSuccess, null, null, warnings);
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
                var errMsg = $"[ERROR] Orchestration pipeline crashed: {ex.Message}";
                _logger.LogCritical(ex, errMsg);
                _logger.LogInformation("OrchestrationCompleted: Success=False");
                return new OrchestratorResult(null, false, errMsg, ex, warnings);
            }
        }
    }
}
