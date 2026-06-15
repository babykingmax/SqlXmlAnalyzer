using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Refactoring.Rules;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public class SqlRefactorEngine
    {
        private readonly List<ISqlRefactorRule> _rules = new();

        public SqlRefactorEngine(bool registerDefaultRules = true)
        {
            if (registerDefaultRules)
            {
                RegisterRule(new TableVariableRefactorRule());
                RegisterRule(new SargableRefactorRule());
                RegisterRule(new ImplicitConversionRefactorRule());
            }
        }

        public void RegisterRule(ISqlRefactorRule rule)
        {
            _rules.Add(rule);
        }

        public IEnumerable<ISqlRefactorRule> Rules => _rules;

        /// <summary>
        /// Refactors the input T-SQL by running all registered rules on its AST.
        /// </summary>
        /// <param name="sql">Original T-SQL code.</param>
        /// <param name="errors">Output parameter containing any T-SQL parse errors.</param>
        /// <returns>Refactored T-SQL code if parsing succeeded; otherwise the original SQL.</returns>
        public string Refactor(string sql, out IList<ParseError> errors)
        {
            var parser = new TSql160Parser(true);
            using (var reader = new StringReader(sql))
            {
                var fragment = parser.Parse(reader, out errors);
                if (errors.Count > 0)
                {
                    return sql; // Return original if compile errors exist
                }

                // Run mutation visitor pipeline
                foreach (var rule in _rules)
                {
                    rule.Apply(fragment);
                }

                // Generate formatted T-SQL
                var generator = new Sql160ScriptGenerator(new SqlScriptGeneratorOptions
                {
                    KeywordCasing = KeywordCasing.Uppercase,
                    MultilineSelectElementsList = false,
                    // AsKeywordOnVariables = true
                });

                generator.GenerateScript(fragment, out string refactoredSql);
                return refactoredSql;
            }
        }
    }
}
