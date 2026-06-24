using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Configuration;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class RuleEngine
    {
        private readonly List<IPlanAnalyzerRule> _rules = new();
        private readonly RuleConfigurationRoot _config;

        public RuleEngine(string? configPath = null)
        {
            ConfigurationLoadResult = RuleConfigurationLoader.Load(configPath);
            _config = ConfigurationLoadResult.Configuration;

            foreach (string warning in ConfigurationLoadResult.Warnings)
            {
                Logger.Warning(warning);
            }

            if (!ConfigurationLoadResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    string.Join(Environment.NewLine, ConfigurationLoadResult.Errors));
            }
        }

        public RuleConfigurationLoadResult ConfigurationLoadResult { get; }
        public IReadOnlyList<IPlanAnalyzerRule> RegisteredRules => _rules;

        public void RegisterRule(IPlanAnalyzerRule rule)
        {
            ArgumentNullException.ThrowIfNull(rule);
            RuleMetadata metadata = rule.Metadata;

            if (_rules.Any(existing =>
                    string.Equals(existing.Metadata.RuleId, metadata.RuleId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Duplicate rule id: {metadata.RuleId}");
            }

            RuleConfig? ruleConfig = FindConfiguration(rule);
            if (ruleConfig != null && !ruleConfig.Enabled)
            {
                Logger.Verbose(
                    $"[RuleEngine] Rule '{rule.Name}' ({metadata.RuleId}) is disabled by configuration.");
                return;
            }

            _rules.Add(rule);
        }

        public List<AnalysisResult> AnalyzePlan(XDocument document, XNamespace ns)
        {
            var results = new List<AnalysisResult>();
            if (document.Root == null)
            {
                return results;
            }

            var temporaryContexts = new List<XElement>();
            try
            {
                List<XElement> relOps = document.Descendants(ns + "RelOp").ToList();
                XElement planContext = relOps.FirstOrDefault()
                    ?? CreateTemporaryContext(document.Root, ns, temporaryContexts);

                foreach (IPlanAnalyzerRule rule in _rules)
                {
                    IEnumerable<XElement> contexts = rule.Metadata.Scope switch
                    {
                        RuleScope.Plan => new[] { planContext },
                        RuleScope.Statement => GetStatementContexts(
                            document,
                            ns,
                            temporaryContexts),
                        RuleScope.Operator => relOps,
                        _ => Array.Empty<XElement>()
                    };

                    foreach (XElement context in contexts)
                    {
                        AnalysisResult? result = ExecuteRule(rule, context, ns);
                        if (result != null)
                        {
                            results.Add(result);
                        }
                    }
                }
            }
            finally
            {
                foreach (XElement context in temporaryContexts)
                {
                    context.Remove();
                }
            }

            return results;
        }

        /// <summary>
        /// Compatibility entry point for operator-detail views. Operator rules run for
        /// every node; plan and statement rules run only for the first applicable node.
        /// </summary>
        public List<AnalysisResult> AnalyzeNode(XElement relOp, XNamespace ns)
        {
            var results = new List<AnalysisResult>();
            XDocument? document = relOp.Document;
            XElement? firstPlanRelOp = document?.Descendants(ns + "RelOp").FirstOrDefault();
            XElement? statement = relOp.Ancestors(ns + "StmtSimple").FirstOrDefault();
            XElement? firstStatementRelOp = statement?.Descendants(ns + "RelOp").FirstOrDefault();

            foreach (IPlanAnalyzerRule rule in _rules)
            {
                bool shouldRun = rule.Metadata.Scope switch
                {
                    RuleScope.Operator => true,
                    RuleScope.Plan => ReferenceEquals(relOp, firstPlanRelOp),
                    RuleScope.Statement => ReferenceEquals(relOp, firstStatementRelOp),
                    _ => false
                };

                if (!shouldRun)
                {
                    continue;
                }

                AnalysisResult? result = ExecuteRule(rule, relOp, ns);
                if (result != null)
                {
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

        private AnalysisResult? ExecuteRule(
            IPlanAnalyzerRule rule,
            XElement context,
            XNamespace ns)
        {
            var stopwatch = Stopwatch.StartNew();
            AnalysisResult? result = rule.Analyze(context, ns);
            stopwatch.Stop();

            if (result == null)
            {
                return null;
            }

            result.Metadata = rule.Metadata;
            RuleConfig? ruleConfig = FindConfiguration(rule);
            if (ruleConfig?.SeverityOverride != null)
            {
                result.Severity = ruleConfig.SeverityOverride;
            }

            Logger.Verbose(
                $"[RuleEngine] Rule '{rule.Name}' hit in {rule.Metadata.Scope} scope " +
                $"on Node {result.NodeId}. Time: {stopwatch.ElapsedMilliseconds}ms");
            return result;
        }

        private RuleConfig? FindConfiguration(IPlanAnalyzerRule rule)
        {
            return _config.Rules.FirstOrDefault(configuration =>
                configuration.RuleId == rule.Metadata.RuleId
                || configuration.RuleId == rule.Name);
        }

        private static IReadOnlyList<XElement> GetStatementContexts(
            XDocument document,
            XNamespace ns,
            List<XElement> temporaryContexts)
        {
            var contexts = new List<XElement>();
            foreach (XElement statement in document.Descendants(ns + "StmtSimple"))
            {
                XElement? context = statement.Descendants(ns + "RelOp").FirstOrDefault();
                contexts.Add(context ?? CreateTemporaryContext(statement, ns, temporaryContexts));
            }
            return contexts;
        }

        private static XElement CreateTemporaryContext(
            XElement parent,
            XNamespace ns,
            List<XElement> temporaryContexts)
        {
            var context = new XElement(ns + "RelOp");
            parent.Add(context);
            temporaryContexts.Add(context);
            return context;
        }
    }
}
