using System;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Rules;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Rules
{
    public class ParameterSensitivityRuleTests
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void QueryRewrite_WithStatisticsUsageOnly_DoesNotSuggestParameterSniffingFixes()
        {
            var doc = CreatePlan(@"
                <OptimizerStatsUsage>
                    <StatisticsInfo Database=""[Db]"" Schema=""[dbo]"" Table=""[T]"" Statistics=""[IX_T]""
                                    LastUpdate=""2026-06-20T00:00:00"" ModificationCount=""0"" SamplingPercent=""100"" />
                </OptimizerStatsUsage>
                <RelOp NodeId=""0"" PhysicalOp=""Index Seek"" />");
            var root = doc.Descendants(Ns + "RelOp").Single();

            var result = new QueryRewriteRule().Analyze(root, Ns);

            result.Should().BeNull();
        }

        [Fact]
        public void QueryRewrite_WithDifferentCompiledAndRuntimeValues_SuggestsParameterSensitivityFixes()
        {
            var doc = CreatePlan(@"
                <ParameterList>
                    <ColumnReference Column=""@p"" ParameterCompiledValue=""(1)"" ParameterRuntimeValue=""(1000)"" />
                </ParameterList>
                <RelOp NodeId=""0"" PhysicalOp=""Index Seek"" />");
            var root = doc.Descendants(Ns + "RelOp").Single();

            var result = new QueryRewriteRule().Analyze(root, Ns);

            result.Should().NotBeNull();
            result!.RuleId.Should().Be("RULE_025_QUERY_REWRITE");
        }

        [Fact]
        public void StatsUsage_WithHealthyStatistics_ReturnsNoIssue()
        {
            string lastUpdate = DateTime.Now.AddDays(-5).ToString("O");
            var doc = CreatePlan($@"
                <OptimizerStatsUsage>
                    <StatisticsInfo Database=""[Db]"" Schema=""[dbo]"" Table=""[T]"" Statistics=""[IX_T]""
                                    LastUpdate=""{lastUpdate}"" ModificationCount=""100"" SamplingPercent=""100"" />
                </OptimizerStatsUsage>
                <RelOp NodeId=""0"" PhysicalOp=""Index Seek"" />");
            var root = doc.Descendants(Ns + "RelOp").Single();

            var result = new StatsUsageRule().Analyze(root, Ns);

            result.Should().BeNull();
        }

        [Fact]
        public void StatsUsage_WithCriticalStatistics_ReturnsCriticalAndOmitsHealthyEntries()
        {
            string lastUpdate = DateTime.Now.AddDays(-5).ToString("O");
            var doc = CreatePlan($@"
                <OptimizerStatsUsage>
                    <StatisticsInfo Database=""[Db]"" Schema=""[dbo]"" Table=""[T]"" Statistics=""[IX_Healthy]""
                                    LastUpdate=""{lastUpdate}"" ModificationCount=""0"" SamplingPercent=""100"" />
                    <StatisticsInfo Database=""[Db]"" Schema=""[dbo]"" Table=""[T]"" Statistics=""[IX_Critical]""
                                    LastUpdate=""{lastUpdate}"" ModificationCount=""12000"" SamplingPercent=""100"" />
                </OptimizerStatsUsage>
                <RelOp NodeId=""0"" PhysicalOp=""Index Seek"" />");
            var root = doc.Descendants(Ns + "RelOp").Single();

            var result = new StatsUsageRule().Analyze(root, Ns);

            result.Should().NotBeNull();
            result!.Severity.Should().Be("Critical");
            result.Message.Should().Contain("IX_Critical");
            result.Message.Should().NotContain("IX_Healthy");
        }

        [Fact]
        public void AnalyzePlan_WithNodeZeroAndNodeOne_ProducesSingleParameterSniffingIssue()
        {
            var doc = CreatePlan(@"
                <ParameterList>
                    <ColumnReference Column=""@p"" ParameterCompiledValue=""(1)"" ParameterRuntimeValue=""(1000)"" />
                </ParameterList>
                <RelOp NodeId=""0"" PhysicalOp=""Nested Loops"" EstimateRows=""1"">
                    <RunTimeInformation>
                        <RunTimeCountersPerThread ActualRows=""2000"" />
                    </RunTimeInformation>
                    <NestedLoops>
                        <RelOp NodeId=""1"" PhysicalOp=""Index Seek"" EstimateRows=""1"" />
                    </NestedLoops>
                </RelOp>");

            var results = PlanDiagnosticAnalyzer.AnalyzePlan(doc, Ns);

            results.Count(result => result.RuleId == "RULE_003_PARAM_SNIFFING").Should().Be(1);
        }

        private static XDocument CreatePlan(string queryPlanContent)
        {
            return XDocument.Parse($@"
                <ShowPlanXML xmlns=""{Ns}"">
                    <BatchSequence>
                        <Batch>
                            <Statements>
                                <StmtSimple StatementText=""SELECT 1"">
                                    <QueryPlan>
                                        {queryPlanContent}
                                    </QueryPlan>
                                </StmtSimple>
                            </Statements>
                        </Batch>
                    </BatchSequence>
                </ShowPlanXML>");
        }
    }
}
