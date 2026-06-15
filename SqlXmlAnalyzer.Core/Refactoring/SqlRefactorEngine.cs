using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Refactoring.Rules;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public class SqlRefactorEngine
    {
        private readonly RuleRegistry _registry = new();
        private readonly int _maxPasses;

        public SqlRefactorEngine(bool registerCoreRules = true, bool registerLegacyRules = false, int maxPasses = 5)
        {
            _maxPasses = maxPasses;
            if (registerCoreRules)
            {
                // Register new modular core rules
                RegisterRule(new IsNullComparisonRefactorRule());
                RegisterRule(new LeftOrSubstringRefactorRule());
                RegisterRule(new TrimRefactorRule());
                RegisterRule(new ConstantFoldingRefactorRule());
            }
            if (registerLegacyRules)
            {
                // Register old/legacy optional rules
                RegisterRule(new TableVariableRefactorRule());
                RegisterRule(new SargableRefactorRule());
                RegisterRule(new ImplicitConversionRefactorRule());
            }
        }

        public void RegisterRule(ISqlRefactorRule rule)
        {
            _registry.Register(rule);
        }

        public IEnumerable<ISqlRefactorRule> Rules => _registry.GetRules();

        /// <summary>
        /// Refactors the input T-SQL by running all registered rules on its AST.
        /// Supports multiple passes until a fixed point is reached or maxPasses is exceeded.
        /// Includes validation to ensure the output remains semantically valid SQL.
        /// </summary>
        public string Refactor(string sql, out IList<ParseError> errors)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                errors = new List<ParseError>();
                return sql;
            }

            var parser = new TSql160Parser(true);
            using (var reader = new StringReader(sql))
            {
                var fragment = parser.Parse(reader, out errors);
                if (errors.Count > 0)
                {
                    return sql; // Return original if compile errors exist
                }

                var context = new RefactorContext(sql);
                string currentSql = sql;
                TSqlFragment currentFragment = fragment;

                for (int pass = 1; pass <= _maxPasses; pass++)
                {
                    bool passChanged = false;
                    var rules = _registry.GetRules();

                    foreach (var rule in rules)
                    {
                        if (rule.CanApply(currentFragment, context))
                        {
                            var beforeSql = GenerateSql(currentFragment);
                            var nextFragment = rule.Apply(currentFragment, context);
                            var afterSql = GenerateSql(nextFragment);

                            if (beforeSql != afterSql)
                            {
                                context.Log($"Pass {pass}: Applied rule [{rule.RuleId}] - {rule.Name}");
                                currentFragment = nextFragment;
                                passChanged = true;
                            }
                        }
                    }

                    if (!passChanged)
                    {
                        break; // Fixed point reached
                    }
                }

                string finalSql = GenerateSql(currentFragment);

                // Validation pass: ensure final SQL parses without error
                IList<ParseError> validationErrors;
                using (var validationReader = new StringReader(finalSql))
                {
                    parser.Parse(validationReader, out validationErrors);
                }

                if (validationErrors.Count > 0)
                {
                    // For safety, if rewriting introduced compile errors, revert to original
                    errors = validationErrors;
                    return sql;
                }

                errors = new List<ParseError>();
                return finalSql;
            }
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
