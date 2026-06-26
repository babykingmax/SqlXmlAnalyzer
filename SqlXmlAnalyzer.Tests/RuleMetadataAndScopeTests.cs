using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Rules;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class RuleMetadataAndScopeTests
    {
        private static readonly XNamespace Ns =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void DefaultRules_HaveUniqueCompleteMetadata()
        {
            var engine = new RuleEngine();
            engine.RegisterDefaultRules();

            IReadOnlyList<IPlanAnalyzerRule> rules = engine.RegisteredRules;
            rules.Should().NotBeEmpty();
            rules.Select(rule => rule.Metadata.RuleId).Should().OnlyHaveUniqueItems();
            rules.Should().OnlyContain(rule =>
                rule.Metadata.RuleId == rule.RuleId
                && !string.IsNullOrWhiteSpace(rule.Metadata.Description)
                && new[] { "Info", "Warning", "Critical" }
                    .Contains(rule.Metadata.DefaultSeverity));
            rules.Select(rule => rule.Metadata.Scope)
                .Should().Contain(new[] { RuleScope.Plan, RuleScope.Statement, RuleScope.Operator });
        }

        [Fact]
        public void DefaultRules_HaveUniqueRuleNumberPrefixes()
        {
            var engine = new RuleEngine();
            engine.RegisterDefaultRules();

            var duplicatePrefixes = engine.RegisteredRules
                .Select(rule => Regex.Match(rule.Metadata.RuleId, @"^RULE_(\d+)_"))
                .Where(match => match.Success)
                .GroupBy(match => match.Groups[1].Value)
                .Where(group => group.Count() > 1)
                .Select(group => $"RULE_{group.Key}")
                .ToList();

            duplicatePrefixes.Should().BeEmpty();
        }

        [Fact]
        public void DefaultConfigurationRuleIds_AreRegisteredRules()
        {
            var engine = new RuleEngine();
            engine.RegisterDefaultRules();
            RuleConfigurationLoadResult configuration = RuleConfigurationLoader.Load();
            var registeredIds = engine.RegisteredRules
                .Select(rule => rule.Metadata.RuleId)
                .ToHashSet(StringComparer.Ordinal);

            configuration.IsSuccess.Should().BeTrue();
            configuration.Configuration.Rules
                .Should().OnlyContain(rule => registeredIds.Contains(rule.RuleId));
        }

        [Fact]
        public void AnalyzePlan_ExecutesRulesAccordingToDeclaredScope()
        {
            var engine = new RuleEngine();
            var planRule = new CountingRule("TEST_PLAN", RuleScope.Plan);
            var statementRule = new CountingRule("TEST_STATEMENT", RuleScope.Statement);
            var operatorRule = new CountingRule("TEST_OPERATOR", RuleScope.Operator);
            engine.RegisterRule(planRule);
            engine.RegisterRule(statementRule);
            engine.RegisterRule(operatorRule);
            XDocument document = XDocument.Parse($"""
                <ShowPlanXML xmlns="{Ns}">
                  <BatchSequence>
                    <Batch>
                      <Statements>
                        <StmtSimple>
                          <QueryPlan>
                            <RelOp NodeId="5"><RelOp NodeId="6" /></RelOp>
                          </QueryPlan>
                        </StmtSimple>
                        <StmtSimple>
                          <QueryPlan>
                            <RelOp NodeId="9"><RelOp NodeId="10" /></RelOp>
                          </QueryPlan>
                        </StmtSimple>
                      </Statements>
                    </Batch>
                  </BatchSequence>
                </ShowPlanXML>
                """);

            List<AnalysisResult> results = engine.AnalyzePlan(document, Ns);

            planRule.InvocationCount.Should().Be(1);
            statementRule.InvocationCount.Should().Be(2);
            operatorRule.InvocationCount.Should().Be(4);
            results.Should().HaveCount(7);
            results.Should().OnlyContain(result => result.Metadata != null);
        }

        [Fact]
        public void AnalyzePlan_PlanRuleDoesNotDependOnNodeZero()
        {
            XDocument document = XDocument.Parse($"""
                <ShowPlanXML xmlns="{Ns}">
                  <BatchSequence><Batch><Statements><StmtSimple>
                    <QueryPlan>
                      <ParameterList>
                        <ColumnReference Column="@p"
                          ParameterCompiledValue="(1)"
                          ParameterRuntimeValue="(1000)" />
                      </ParameterList>
                      <RelOp NodeId="17" EstimateRows="1">
                        <RunTimeInformation>
                          <RunTimeCountersPerThread ActualRows="2000" />
                        </RunTimeInformation>
                        <RelOp NodeId="18" />
                      </RelOp>
                    </QueryPlan>
                  </StmtSimple></Statements></Batch></BatchSequence>
                </ShowPlanXML>
                """);

            List<AnalysisResult> results = PlanDiagnosticAnalyzer.AnalyzePlan(document, Ns);

            results.Count(result => result.RuleId == "RULE_003_PARAM_SNIFFING")
                .Should().Be(1);
            results.Single(result => result.RuleId == "RULE_003_PARAM_SNIFFING")
                .Metadata!.Scope.Should().Be(RuleScope.Plan);
        }

        [Fact]
        public void RegisterRule_WithDuplicateRuleId_Throws()
        {
            var engine = new RuleEngine();
            engine.RegisterRule(new CountingRule("TEST_DUPLICATE", RuleScope.Plan));

            Action action = () =>
                engine.RegisterRule(new CountingRule("TEST_DUPLICATE", RuleScope.Operator));

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*Duplicate rule id*");
        }

        private sealed class CountingRule : IPlanAnalyzerRule
        {
            public CountingRule(string ruleId, RuleScope scope)
            {
                RuleId = ruleId;
                Metadata = new RuleMetadata(
                    ruleId,
                    RuleCategory.AntiPattern,
                    scope,
                    "Warning",
                    "Test rule");
            }

            public int InvocationCount { get; private set; }
            public string RuleId { get; }
            public string Name => RuleId;
            public string Description => Metadata.Description;
            public RuleMetadata Metadata { get; }

            public AnalysisResult Analyze(XElement relOp, XNamespace ns)
            {
                InvocationCount++;
                return new AnalysisResult
                {
                    RuleId = RuleId,
                    Severity = "Warning",
                    Title = RuleId,
                    Message = "hit",
                    NodeId = relOp.Attribute("NodeId")?.Value ?? string.Empty
                };
            }
        }
    }
}
