using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Rules
{
    public partial class RuleEngine
    {
        private readonly List<IPlanAnalyzerRule> _rules = new();
        private readonly RuleConfigurationRoot _config;
        private readonly Diagnostics.IUnexpectedErrorReporter _unexpectedErrors;
        private readonly Dictionary<IPlanAnalyzerRule, RuleMetadata> _metadata = new();

        public RuleEngine(string? configPath = null, Diagnostics.IUnexpectedErrorReporter? unexpectedErrors = null,
            RuleConfigurationDocument? configuration = null)
        {
            _unexpectedErrors = unexpectedErrors ?? Diagnostics.UnexpectedErrorReporter.Shared;
            ConfigurationLoadResult = configuration == null ? RuleConfigurationLoader.Load(configPath, _unexpectedErrors)
                : new(configuration.ToLegacy(), "会话配置快照", configuration.Warnings, [], false) { Document = configuration };
            _config = new RuleConfigurationRoot { Rules = ConfigurationLoadResult.Configuration.Rules.Select(c =>
                new RuleConfig { RuleId = c.RuleId, Enabled = c.Enabled, SeverityOverride = c.SeverityOverride }).ToList() };

            foreach (string warning in ConfigurationLoadResult.Warnings)
            {
                Logger.Warning(warning);
            }

            if (!ConfigurationLoadResult.IsSuccess)
            {
                throw new System.IO.InvalidDataException(
                    string.Join(Environment.NewLine, ConfigurationLoadResult.Errors));
            }
        }

        public RuleConfigurationLoadResult ConfigurationLoadResult { get; }
        public IReadOnlyList<IPlanAnalyzerRule> RegisteredRules => _rules.AsReadOnly();

        public void RegisterRule(IPlanAnalyzerRule rule)
        {
            ArgumentNullException.ThrowIfNull(rule);
            RuleMetadata metadata = rule.Metadata;
            if (metadata.RuleId != rule.RuleId || string.IsNullOrWhiteSpace(metadata.RuleId)
                || string.IsNullOrWhiteSpace(metadata.Version) || !Enum.IsDefined(metadata.Scope))
                throw new InvalidOperationException("RULE_METADATA_INVALID: RuleId 和版本必须有效。");

            if (_rules.Any(existing =>
                    string.Equals(_metadata[existing].RuleId, metadata.RuleId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Duplicate rule id: {metadata.RuleId}");
            }

            RuleConfig? ruleConfig = FindConfiguration(rule);
            if (ruleConfig != null && !ruleConfig.Enabled)
            {
                Logger.Verbose(
                    $"[RuleEngine] Rule '{rule.Name}' ({metadata.RuleId}) is disabled by configuration.");
                // Keep the registration so every disabled rule has a visible Skipped run.
            }

            _rules.Add(rule);
            _metadata.Add(rule, metadata);
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

        private RuleConfig? FindConfiguration(IPlanAnalyzerRule rule)
        {
            return _config.Rules.FirstOrDefault(configuration =>
                configuration.RuleId == (_metadata.TryGetValue(rule, out var metadata) ? metadata.RuleId : rule.Metadata.RuleId)
                || configuration.RuleId == rule.Name);
        }

    }
}
