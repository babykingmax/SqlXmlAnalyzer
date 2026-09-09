using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Privacy;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Refactoring.Rules;

namespace SqlXmlAnalyzer.CLI;

internal static class SqlSemanticCommand
{
    public static int Run(string command, string[] args, CancellationToken token)
    {
        if (args.Contains("--help") || args.Contains("-h")) { Usage(); return 0; }
        string? output = null;
        object? completedResult = null;
        try
        {
            if (args.Length == 0 || args[0].StartsWith('-')) throw new InvalidDataException("需要源 SQL 文件路径。");
            string path = args[0];
            string? scenarios = null, candidatePath = null, sourceHash = null, previewHash = null, suiteHash = null;
            bool acknowledge = false, showSql = false;
            var selected = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                string option = args[i];
                if (option != "--select" && !seen.Add(option)) throw new InvalidDataException("选项重复：" + option);
                if (option == "--acknowledge-scenarios") { acknowledge = true; continue; }
                if (option == "--show-sql") { showSql = true; continue; }
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-')) throw new InvalidDataException("选项缺少值：" + option);
                string value = args[++i];
                switch (option)
                {
                    case "--scenarios": scenarios = value; break;
                    case "--candidate": candidatePath = value; break;
                    case "--select": selected.Add(value); break;
                    case "--review-source-hash": sourceHash = value; break;
                    case "--review-preview-hash": previewHash = value; break;
                    case "--review-suite-hash": suiteHash = value; break;
                    case "--output": output = value; break;
                    default: throw new InvalidDataException("未知选项：" + option);
                }
            }
            bool compare = command == "semantic-compare", apply = command == "rewrite-apply";
            if (scenarios == null || (compare ? candidatePath == null || selected.Count != 0 : candidatePath != null || selected.Count == 0) ||
                (!apply && (acknowledge || sourceHash != null || previewHash != null || suiteHash != null)) ||
                (apply && (!acknowledge || sourceHash == null || previewHash == null || suiteHash == null)))
                throw new InvalidDataException("缺少场景/选择或审核参数，或选项不适用于此命令；请查看 --help。");
            SqlReportPathGuard.Validate(path, output);
            SqlReportPathGuard.Validate(scenarios, output);
            if (candidatePath != null) SqlReportPathGuard.Validate(candidatePath, output);
            var suite = SqlSemanticSandboxPolicy.Load(scenarios);
            var files = new SqlWritebackService(new PhysicalSqlWritebackFileSystem());
            var source = files.ReadSnapshot(path, SqlSemanticSandboxPolicy.MaxSqlCharacters, token);
            SqlSemanticSandboxPolicy.ParseBatches(source.Text);
            var runner = new LocalDbSqlSemanticRunner();
            object result;
            bool success;
            if (compare)
            {
                var candidate = files.ReadSnapshot(candidatePath!, SqlSemanticSandboxPolicy.MaxSqlCharacters, token);
                var validation = runner.ValidateAsync(source.Text, candidate.Text, suite, token).GetAwaiter().GetResult();
                result = new { Privacy = OutputPrivacy.RawNotice, Validation = validation, SourceWritten = false,
                    OriginalSql = showSql ? source.Text : null, CandidateSql = showSql ? candidate.Text : null };
                success = validation.CanPrepareApply;
            }
            else
            {
                var engine = new SqlRefactoringEngine([new ConstantFoldingRefactorRule(), new IsNullComparisonRefactorRule(),
                    new LeftOrSubstringRefactorRule(), new TrimRefactorRule(), new ImplicitConversionRefactorRule(),
                    new SubqueryToJoinRule(), new ExistsToJoinRule(), new TableVariableRefactorRule(), new ScalarSubqueryToJoinRule()],
                    new DefaultRuleFilter(), NullLogger<SqlRefactoringEngine>.Instance);
                var candidate = engine.Run(source.Text, new AnalysisReport([]), new(SelectedProposalIds: selected), true);
                if (!candidate.IsSuccess || candidate.Review == null)
                    throw new InvalidDataException("无法重新生成所选提案：" + string.Join("; ", candidate.Errors));
                var review = candidate.Review;
                if (apply && (sourceHash != review.SourceHash || previewHash != SqlTextHash.Compute(review.PreviewSql) || suiteHash != suite.Fingerprint))
                    throw new InvalidDataException("审核 hash 与本次源 SQL/所选预览不一致，禁止验证和应用。");
                var service = new ReviewedSqlApplyService(files, runner);
                var prepared = service.PrepareAsync(path, source.Text, review, suite, token).GetAwaiter().GetResult();
                var applied = apply && prepared.CanApply ? service.Apply(prepared.Prepared!, review, acknowledge, token) : null;
                result = new
                {
                    Privacy = OutputPrivacy.RawNotice, review.SourceHash, PreviewHash = SqlTextHash.Compute(review.PreviewSql),
                    SelectedProposalIds = review.Proposals.Where(p => p.IsSelected).Select(p => p.Id),
                    PreviewSql = showSql ? review.PreviewSql : null, Validation = prepared.Validation,
                    CanApply = !apply && prepared.CanApply, Error = applied?.Error ?? prepared.Error,
                    SourceWritten = applied?.SourceWritten == true, CommitOutcomeUnknown = applied?.CommitOutcomeUnknown == true,
                    Writeback = applied?.Writeback is { } writeback ? new
                    {
                        writeback.IsSuccess, writeback.SourceWritten, writeback.CommitOutcomeUnknown, writeback.IsCanceled,
                        Stage = writeback.Stage.ToString(), writeback.BackupVerified, writeback.BackupPath, writeback.TemporaryPath,
                        writeback.SourceSha256, writeback.OutputSha256, writeback.ErrorMessage, writeback.Warnings, writeback.Diagnostics
                    } : null,
                    Diagnostic = applied?.Diagnostic ?? prepared.Diagnostic
                };
                success = apply ? applied?.IsSuccess == true : prepared.CanApply;
            }
            completedResult = result;
            // Recheck every input alias after database work; a report must not overwrite an input.
            SqlReportPathGuard.Validate(path, output); SqlReportPathGuard.Validate(scenarios, output);
            if (candidatePath != null) SqlReportPathGuard.Validate(candidatePath, output);
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            if (output == null) Console.WriteLine(json);
            else { File.WriteAllText(output, json); Console.WriteLine("报告已写入到: " + output); }
            // A committed write must retain its outcome even if cancellation arrives afterwards.
            return success ? 0 : token.IsCancellationRequested ? 130 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(ExceptionPolicy.Describe(exception, "CLI.SemanticRewrite"));
            if (completedResult != null)
                Console.WriteLine(JsonSerializer.Serialize(new { ReportOutputFailed = true, Result = completedResult }, new JsonSerializerOptions { WriteIndented = true }));
            return exception is OperationCanceledException ? 130 : exception is InvalidDataException ? 2 : 1;
        }
    }

    private static void Usage()
    {
        Console.WriteLine("semantic-compare <original.sql> --candidate <candidate.sql> --scenarios <suite.json> [--show-sql] [--output <report.json>]");
        Console.WriteLine("rewrite-validate <source.sql> --select <ID> [--select <ID>...] --scenarios <suite.json> [--show-sql] [--output <report.json>]");
        Console.WriteLine("rewrite-apply <source.sql> --select <ID> [--select <ID>...] --scenarios <suite.json> --review-source-hash <SHA256> --review-preview-hash <SHA256> --review-suite-hash <SHA256> --acknowledge-scenarios [--output <report.json>]");
        Console.WriteLine("仅使用专用 SqlXmlAnalyzer_* LocalDB；验证只说明所列场景。apply 会重新执行数据库验证，校验源/选择后备份并写回；不接受历史报告作为凭据。");
    }
}
