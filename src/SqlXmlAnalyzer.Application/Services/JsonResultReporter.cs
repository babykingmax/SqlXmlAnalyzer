using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Application.Models;

namespace SqlXmlAnalyzer.Application.Services
{
    public class JsonResultReporter : IResultReporter
    {
        public bool ShowSql { get; set; }

        public void Report(RefactorResult result)
        {
            Report(result, false, null);
        }

        public void Report(RefactorResult result, bool isDryRun, string? outputPath = null)
        {
            if (result == null) return;

            var context = result.Context;
            var reportDto = new RefactorReportDto
            {
                IsSuccess = result.IsSuccess,
                IsDryRun = isDryRun,
                HasChanges = context?.Changed ?? false,
                SourceWritten = result.SourceWritten,
                Outcome = result.Outcome.ToString(),
                SourceHash = result.Review?.SourceHash,
                Review = result.Review == null ? null : new
                {
                    result.Review.SourceHash,
                    result.Review.CanApply,
                    result.Review.Validation,
                    result.Review.Warnings,
                    PreviewSql = ShowSql ? result.Review.PreviewSql : null,
                    Proposals = result.Review.Proposals.Select(p => new
                    {
                        p.Id, p.SourceHash, p.BaseSqlHash, p.CandidateHash, p.RuleId, p.RuleVersion,
                        p.Description, p.IsSelected, p.DependsOn, p.Preconditions, p.Risks, p.Evidence,
                        p.Warnings, p.Validation,
                        Diff = ShowSql ? p.Diff : null
                    }).ToArray()
                },
                TimeElapsedMs = result.TimeElapsedMs,
                OriginalSqlLength = context?.OriginalSql?.Length ?? 0,
                RefactoredSqlLength = result.OutputSql?.Length ?? 0,
                PassesCount = result.PassesCount,
                Changes = context?.RefactorChanges.Select(c => new ChangeDto
                {
                    RuleId = c.RuleId,
                    Description = c.Description,
                    Timestamp = c.Timestamp
                }).ToList() ?? new(),
                Failures = context?.RefactorFailures.Select(f => new FailureDto
                {
                    RuleId = f.RuleId,
                    ExceptionName = f.ExceptionName,
                    StackTrace = f.StackTrace,
                    Timestamp = f.Timestamp
                }).ToList() ?? new(),
                Warnings = context?.Warnings?.ToList() ?? new(),
                SafetySkips = context?.SafetySkips.ToList() ?? new(),
                Diagnostic = result.Diagnostic,
                Diagnostics = context?.Analysis.Diagnostics,
                Errors = result.Errors?.ToList() ?? new(),
                ParseErrors = result.ParseErrors?.Select(pe => new ParseErrorDto
                {
                    Line = pe.Line,
                    Column = pe.Column,
                    Message = pe.Message
                }).ToList(),
                OriginalSql = ShowSql ? context?.OriginalSql : null,
                RefactoredSql = ShowSql ? result.OutputSql : null
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string jsonString = JsonSerializer.Serialize(reportDto, jsonOptions);

            if (!string.IsNullOrEmpty(outputPath))
            {
                try
                {
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(outputPath, jsonString, Encoding.UTF8);
                    Console.WriteLine($"报告已写入到: {outputPath}");
                }
                catch (Exception ex)
                {
                    Core.Diagnostics.ExceptionPolicy.Describe(ex, "JsonResultReporter");
                    throw;
                }
            }
            else
            {
                Console.WriteLine(jsonString);
            }
        }
    }
}
