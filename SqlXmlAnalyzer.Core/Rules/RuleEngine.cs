using System.Collections.Generic;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class RuleEngine
    {
        private readonly List<IPlanAnalyzerRule> _rules = new();

        public void RegisterRule(IPlanAnalyzerRule rule)
        {
            _rules.Add(rule);
        }

        public List<AnalysisResult> AnalyzeNode(XElement relOp, XNamespace ns)
        {
            var results = new List<AnalysisResult>();
            foreach (var rule in _rules)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = rule.Analyze(relOp, ns);
                sw.Stop();

                if (result != null)
                {
                    Logger.Verbose($"[RuleEngine] Rule '{rule.Name}' hit on Node {result.NodeId}. Time: {sw.ElapsedMilliseconds}ms");
                    results.Add(result);
                }
            }
            return results;
        }

        public void RegisterDefaultRules()
        {
            RegisterRule(new ImplicitConversionRule());
            RegisterRule(new KeyLookupRule());
            RegisterRule(new ParameterSniffingRule());
            RegisterRule(new RowEstimateMismatchRule());
            RegisterRule(new MemoryGrantRule());
            RegisterRule(new ResidualPredicateRule());
            RegisterRule(new SpillDetectionRule());
            RegisterRule(new ParallelSkewRule());
            RegisterRule(new UdfAndTableVariableRule());
            RegisterRule(new NestedLoopsHighExecRule());
            RegisterRule(new AntiPatternRule());
            RegisterRule(new SerialPlanReasonRule());
            RegisterRule(new LocalVariablesRule());
            RegisterRule(new ZeroRowActualsRule());
        }
    }
}
