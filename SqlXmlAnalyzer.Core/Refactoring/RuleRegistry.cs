using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    internal class RuleRegistry
    {
        private readonly List<ISqlRefactorRule> _rules = new();

        public void Register(ISqlRefactorRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (_rules.Any(r => r.RuleId == rule.RuleId))
                throw new InvalidOperationException($"Rule with ID {rule.RuleId} is already registered.");
            _rules.Add(rule);
        }

        public void Unregister(string ruleId)
        {
            _rules.RemoveAll(r => r.RuleId == ruleId);
        }

        public IEnumerable<ISqlRefactorRule> GetRules()
        {
            return _rules.OrderBy(r => r.Priority);
        }

        public void RegisterFromAssembly(Assembly assembly)
        {
            var ruleTypes = assembly.GetTypes()
                .Where(t => typeof(ISqlRefactorRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in ruleTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is ISqlRefactorRule rule)
                    {
                        Register(rule);
                    }
                }
                catch
                {
                    // Ignore rules that can't be instantiated without parameters (if any)
                }
            }
        }
    }
}
