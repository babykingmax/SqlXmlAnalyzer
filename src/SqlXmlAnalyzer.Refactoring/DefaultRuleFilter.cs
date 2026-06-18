using System.Collections.Generic;
using System.Linq;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Refactoring
{
    public class DefaultRuleFilter : IRuleFilter
    {
        /// <summary>
        /// Filters and orders the refactoring rules.
        /// If both EnabledRuleIds and DisabledRuleIds are null, all rules are enabled by default.
        /// </summary>
        public IEnumerable<ISqlRefactorRule> Filter(IEnumerable<ISqlRefactorRule> rules, RefactorOptions options)
        {
            return rules
                .Where(r => (options.EnabledRuleIds == null || options.EnabledRuleIds.Contains(r.RuleId)) &&
                            (options.DisabledRuleIds == null || !options.DisabledRuleIds.Contains(r.RuleId)))
                .OrderByDescending(r => r.Priority);
        }
    }
}
