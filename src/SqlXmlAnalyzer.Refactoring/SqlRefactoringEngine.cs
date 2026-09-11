using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Refactoring;

public class SqlRefactoringEngine : IRefactoringEngine
{
    private readonly IEnumerable<ISqlRefactorRule> _rules;
    private readonly IRuleFilter _ruleFilter;
    private readonly ILogger<SqlRefactoringEngine> _logger;
    private readonly IUnexpectedErrorReporter _unexpectedErrors;

    public SqlRefactoringEngine(IEnumerable<ISqlRefactorRule> rules, IRuleFilter ruleFilter,
        ILogger<SqlRefactoringEngine> logger, IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        _rules = rules;
        _ruleFilter = ruleFilter;
        _logger = logger;
        _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
    }

    public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun) =>
        Run(sql, report, options, isDryRun, System.Threading.CancellationToken.None);

    public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun, System.Threading.CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var context = new RefactorContext(sql, report, isDryRun);
        var errors = new List<string>();
        var proposals = new List<RewriteProposal>();
        var proposalService = new RewriteProposalService(_unexpectedErrors);
        string proposalSql = sql;
        int passCount = 0;
        RefactorResult Failure(string message, IReadOnlyList<ParseError>? parseErrors = null,
            UnexpectedErrorReport? diagnostic = null)
        {
            context.DiscardChanges();
            errors.Add(message);
            return new RefactorResult(sql, false, errors, context, parseErrors)
            {
                TimeElapsedMs = timer.Elapsed.TotalMilliseconds, PassesCount = passCount, Diagnostic = diagnostic
            };
        }
        RefactorResult ExceptionFailure(Exception exception, string operation)
        {
            if (exception is OperationCanceledException)
            {
                _logger.LogWarning("RefactorCanceled: Operation={Operation}", operation);
                return Failure("重构已取消，保留原 SQL。");
            }
            if (ExceptionPolicy.IsExpected(exception))
            {
                _logger.LogError(exception, "RefactorFailed: Operation={Operation}", operation);
                return Failure($"重构失败，保留原 SQL：{ExceptionPolicy.Message(exception)}");
            }
            var diagnostic = ExceptionPolicy.Capture(exception, operation, _unexpectedErrors);
            _logger.LogCritical(exception, "UnexpectedRefactorFailure: Operation={Operation}, Dump={Dump}", operation, diagnostic.DumpPath);
            return Failure($"重构发生未知错误，保留原 SQL：{ExceptionPolicy.Message(exception)}；{diagnostic.Summary}", diagnostic: diagnostic);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.MaxPasses < 1)
            {
                _logger.LogError("Invalid refactoring pass limit.");
                return Failure("MaxPasses 必须大于 0。");
            }
            _logger.LogDebug("RefactorStarted: DryRun={DryRun}, SqlLength={SqlLength}", isDryRun, sql.Length);
            var parser = new TSql160Parser(true);
            using var reader = new StringReader(sql);
            var fragment = parser.Parse(reader, out var parseErrors);
            cancellationToken.ThrowIfCancellationRequested();
            if (parseErrors.Count > 0)
            {
                _logger.LogError("ParserFailed: Count={Count}", parseErrors.Count);
                errors.AddRange(parseErrors.Select(error => $"Line {error.Line}, Col {error.Column}: {error.Message}"));
                return Failure("输入 SQL 语法错误，保留原 SQL。", parseErrors.ToList());
            }

            var activeRules = _ruleFilter.Filter(_rules, options).ToList();
            var currentFragment = Rules.SqlNodeCloner.Clone(fragment) ?? fragment;
            for (int pass = 1; pass <= options.MaxPasses; pass++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                passCount = pass;
                bool passChanged = false;
                foreach (var rule in activeRules)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!rule.CanApply(currentFragment, context)) continue;
                        int skipsBefore = context.SafetySkips.Count;
                        var result = rule.Apply(currentFragment, context);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (context.SafetySkips.Count > skipsBefore)
                            _logger.LogWarning("UnsafeRewriteSkipped: RuleId={RuleId}, Reason={Reason}",
                                rule.RuleId, context.SafetySkips[^1].ReasonCode);
                        if (result.IsApplied)
                        {
                            currentFragment = result.Fragment;
                            string nextSql = GenerateSql(currentFragment);
                            if (string.Equals(nextSql, proposalSql, StringComparison.Ordinal)) continue;
                            var proposal = proposalService.Propose(sql, proposalSql, nextSql, rule.RuleId, rule.RuleVersion,
                                result.ChangeDescription ?? rule.Description, proposals.Select(p => p.Id), context);
                            if (!proposal.Validation.IsValid)
                            {
                                _logger.LogError("RewriteProposalValidationFailed: Rule={RuleId}", rule.RuleId);
                                return Failure("提案校验失败，保留原 SQL：" + string.Join("; ", proposal.Validation.Errors));
                            }
                            proposals.Add(proposal);
                            proposalSql = nextSql;
                            context.RecordChange(rule.RuleId, result.ChangeDescription ?? $"Applied rule {rule.RuleId}");
                            passChanged = true;
                            _logger.LogDebug("RuleApplied: RuleId={RuleId}, Pass={Pass}", rule.RuleId, pass);
                        }
                    }
                    catch (Exception exception)
                    {
                        context.RecordFailure(rule.RuleId, exception.GetType().Name, ExceptionPolicy.Details(exception));
                        // A rule can mutate its AST before throwing. Never return a partial candidate.
                        return ExceptionFailure(exception, $"RefactorRule:{rule.RuleId}");
                    }
                }
                if (!passChanged) break;
            }

            // Safety-only observations must not trigger formatting-only file writes or backups.
            string finalSql = proposalSql;
            using var validationReader = new StringReader(finalSql);
            parser.Parse(validationReader, out var validationErrors);
            cancellationToken.ThrowIfCancellationRequested();
            if (validationErrors.Count > 0)
            {
                _logger.LogError("RefactorValidationFailed: Count={Count}", validationErrors.Count);
                return Failure("候选 SQL 存在语法错误，已丢弃候选并保留原 SQL。", validationErrors.ToList());
            }
            _logger.LogDebug("RefactorFinished: Changes={Changes}, SafetySkips={SafetySkips}", context.RefactorChanges.Count, context.SafetySkips.Count);
            return new RefactorResult(finalSql, true, errors, context)
            {
                TimeElapsedMs = timer.Elapsed.TotalMilliseconds, PassesCount = passCount,
                Review = proposalService.Review(sql, proposals, options.SelectedProposalIds, context.Warnings)
            };
        }
        catch (Exception exception)
        {
            return ExceptionFailure(exception, "RefactoringPipeline");
        }
    }

    private static string GenerateSql(TSqlFragment fragment)
    {
        var generator = new Sql160ScriptGenerator(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase, MultilineSelectElementsList = false
        });
        generator.GenerateScript(fragment, out string script);
        return script;
    }
}
