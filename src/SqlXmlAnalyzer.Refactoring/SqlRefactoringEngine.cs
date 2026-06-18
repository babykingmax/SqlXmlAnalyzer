using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Refactoring
{
    public class SqlRefactoringEngine : IRefactoringEngine
    {
        private readonly IEnumerable<ISqlRefactorRule> _rules;
        private readonly IRuleFilter _ruleFilter;
        private readonly ILogger<SqlRefactoringEngine> _logger;

        public SqlRefactoringEngine(
            IEnumerable<ISqlRefactorRule> rules,
            IRuleFilter ruleFilter,
            ILogger<SqlRefactoringEngine> logger)
        {
            _rules = rules;
            _ruleFilter = ruleFilter;
            _logger = logger;
        }

        public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var context = new RefactorContext(sql, report, isDryRun);
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(sql))
            {
                sw.Stop();
                return new RefactorResult(sql, true, errors, context) { TimeElapsedMs = sw.Elapsed.TotalMilliseconds, PassesCount = 0 };
            }

            _logger.LogInformation("RefactorStarted: DryRun={DryRun}", isDryRun);

            var parser = new TSql160Parser(true);
            TSqlFragment fragment;
            using (var reader = new StringReader(sql))
            {
                fragment = parser.Parse(reader, out var parseErrors);
                if (parseErrors != null && parseErrors.Count > 0)
                {
                    foreach (var err in parseErrors)
                    {
                        errors.Add($"Line {err.Line}, Col {err.Column}: {err.Message}");
                    }
                    _logger.LogError("ParserFailed: Errors={Errors}", string.Join("; ", errors));
                    sw.Stop();
                    return new RefactorResult(sql, false, errors, context, parseErrors != null ? new List<ParseError>(parseErrors) : null) { TimeElapsedMs = sw.Elapsed.TotalMilliseconds, PassesCount = 0 };
                }
            }

            var activeRules = _ruleFilter.Filter(_rules, options).ToList();
            var currentFragment = Rules.SqlNodeCloner.Clone(fragment) ?? fragment;
            int passCount = 0;

            for (int pass = 1; pass <= options.MaxPasses; pass++)
            {
                passCount = pass;
                bool passChanged = false;
                var rulesAppliedThisPass = new List<string>();

                foreach (var rule in activeRules)
                {
                    try
                    {
                        if (rule.CanApply(currentFragment, context))
                        {
                            var result = rule.Apply(currentFragment, context);
                            if (result.IsApplied)
                            {
                                currentFragment = result.Fragment;
                                var description = result.ChangeDescription ?? $"Applied rule {rule.Name} ({rule.RuleId})";
                                context.RecordChange(rule.RuleId, description);
                                rulesAppliedThisPass.Add(rule.RuleId);
                                passChanged = true;

                                _logger.LogInformation("RuleApplied: RuleId={RuleId}, Description={Description}", rule.RuleId, description);
                            }
                            else
                            {
                                _logger.LogInformation("RuleSkipped: RuleId={RuleId}, Reason=Apply returned not applied", rule.RuleId);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("RuleSkipped: RuleId={RuleId}, Reason=CanApply returned false", rule.RuleId);
                        }
                    }
                    catch (Exception ex)
                    {
                        context.RecordFailure(rule.RuleId, ex.GetType().Name, ex.StackTrace ?? ex.Message);
                        _logger.LogError(ex, "RuleFailed: RuleId={RuleId}, Error={ErrorMessage}", rule.RuleId, ex.Message);
                    }
                }

                if (rulesAppliedThisPass.Count > 0)
                {
                    _logger.LogInformation("PassCompleted: Pass={Pass}, RulesApplied={RulesApplied}", pass, string.Join(", ", rulesAppliedThisPass));
                }

                if (!passChanged)
                {
                    break; // Fixed point reached
                }
            }

            string finalSql = GenerateSql(currentFragment);

            // Validation pass: ensure refactored SQL parses without error
            using (var validationReader = new StringReader(finalSql))
            {
                parser.Parse(validationReader, out var validationErrors);
                if (validationErrors != null && validationErrors.Count > 0)
                {
                    var validationMsg = "Refactored SQL has syntax errors. Reverting changes.";
                    errors.Add(validationMsg);
                    _logger.LogError("ValidationFailed: Message={Message}", validationMsg);
                    sw.Stop();
                    return new RefactorResult(sql, false, errors, context, validationErrors != null ? new List<ParseError>(validationErrors) : null) { TimeElapsedMs = sw.Elapsed.TotalMilliseconds, PassesCount = passCount };
                }
            }

            sw.Stop();
            _logger.LogInformation("RefactorFinished: ChangesCount={ChangesCount}", context.RefactorChanges.Count);
            return new RefactorResult(finalSql, true, errors, context) { TimeElapsedMs = sw.Elapsed.TotalMilliseconds, PassesCount = passCount };
        }

        private string GenerateSql(TSqlFragment fragment)
        {
            var generator = new Sql160ScriptGenerator(new SqlScriptGeneratorOptions
            {
                KeywordCasing = KeywordCasing.Uppercase,
                MultilineSelectElementsList = false
            });

            generator.GenerateScript(fragment, out string script);
            return script;
        }
    }
}
