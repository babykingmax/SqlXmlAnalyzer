using System.Collections.Generic;
using System.Xml.Linq;

using SqlXmlAnalyzer.Core.Configuration;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class RuleEngine
    {
        private readonly List<IPlanAnalyzerRule> _rules = new();
        private RuleConfigurationRoot _config;

        public RuleEngine(string configPath = "RuleConfiguration.json")
        {
            _config = RuleConfigurationLoader.Load(configPath);
        }

        public void RegisterRule(IPlanAnalyzerRule rule)
        {
            var ruleConfig = _config.Rules.FirstOrDefault(r => r.RuleId == rule.RuleId || r.RuleId == rule.Name);
            if (ruleConfig != null && !ruleConfig.Enabled)
            {
                Logger.Verbose($"[RuleEngine] Rule '{rule.Name}' ({rule.RuleId}) is disabled by configuration.");
                return; // Skip registration if disabled
            }
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
                    var ruleConfig = _config.Rules.FirstOrDefault(r => r.RuleId == rule.RuleId || r.RuleId == rule.Name);
                    if (ruleConfig?.SeverityOverride != null)
                    {
                        result.Severity = ruleConfig.SeverityOverride;
                    }

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
            RegisterRule(new LargeMemoryGrantRule());
            RegisterRule(new ResidualPredicateRule());
            RegisterRule(new SpillDetectionRule());
            RegisterRule(new ParallelSkewRule());
            RegisterRule(new UdfAndTableVariableRule());
            RegisterRule(new NestedLoopsHighExecRule());
            RegisterRule(new AntiPatternRule());
            RegisterRule(new SerialPlanReasonRule());
            RegisterRule(new LocalVariablesRule());
            RegisterRule(new ZeroRowActualsRule());
            RegisterRule(new WaitStatsRule());
            RegisterRule(new ResourceSemaphoreRule());
            RegisterRule(new OptimizerAbortRule());
            RegisterRule(new CacheAndRecompileRule());
            RegisterRule(new MissingIndexRule());
            RegisterRule(new TableScanRule());
            RegisterRule(new HighCostOperatorRule());
            RegisterRule(new NestedLoopsRunningTotalRule());
            RegisterRule(new MultipleScalarSubqueriesRule());
            RegisterRule(new QueryRewriteRule());
            RegisterRule(new ImplicitConversionDocRule());
            RegisterRule(new ParameterSniffingDocRule());
            RegisterRule(new StatsUsageRule());
            RegisterRule(new MemoryGrantDocRule());
            RegisterRule(new CardinalityErrorRule());
            RegisterRule(new KeyLookupOpRule());
            RegisterRule(new MemorySpillRule());
            RegisterRule(new ThreadSkewRule());
            RegisterRule(new ResidualPredOpRule());
            RegisterRule(new SargableIndexRecommendationRule());
        }
    }
}
