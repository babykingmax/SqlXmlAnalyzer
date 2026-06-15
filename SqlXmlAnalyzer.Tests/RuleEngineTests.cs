using System.Linq;
using System.Xml.Linq;
using Xunit;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer.Tests
{
    public class RuleEngineTests
    {
        private static readonly XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void ImplicitConversionRule_ShouldDetectConversion()
        {
            var rule = new ImplicitConversionRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""1"" PhysicalOp=""Clustered Index Scan"">
                            <ScalarOperator ScalarString=""CONVERT_IMPLICIT(varchar(10), [ColA], 0)"" />
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_001_IMPLICIT_CONV", result.RuleId);
            Assert.Equal("Critical", result.Severity);
            Assert.Contains("隐式转换", result.Title);
            Assert.Contains("CONVERT_IMPLICIT", result.Message);
        }

        [Fact]
        public void KeyLookupRule_ShouldDetectKeyLookup()
        {
            var rule = new KeyLookupRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""2"" PhysicalOp=""Key Lookup"">
                            <IndexScan>
                                <Object Table=""[dbo].[Users]"" />
                            </IndexScan>
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_002_KEY_LOOKUP", result.RuleId);
            Assert.Contains("回表查询", result.Title);
            Assert.Contains("Users", result.Message);
        }

        [Fact]
        public void ParameterSniffingRule_ShouldDetectMismatch()
        {
            var rule = new ParameterSniffingRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""0"">
                            <QueryPlan>
                                <ParameterList>
                                    <ColumnReference Column=""@p1"" ParameterCompiledValue=""'A'"" ParameterRuntimeValue=""'B'"" />
                                </ParameterList>
                            </QueryPlan>
                         </RelOp>";
            var doc = XDocument.Parse(xml);
            var element = doc.Root!;

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_003_PARAM_SNIFFING", result.RuleId);
            Assert.Contains("参数嗅探", result.Title);
            Assert.Contains("'A'", result.Message);
            Assert.Contains("'B'", result.Message);
        }

        [Fact]
        public void RowEstimateMismatchRule_ShouldDetectCriticalMismatch()
        {
            var rule = new RowEstimateMismatchRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""3"" EstimateRows=""1"">
                            <RunTimeInformation>
                                <RunTimeCountersPerThread ActualRows=""200"" ActualExecutions=""1"" />
                            </RunTimeInformation>
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_004_ESTIMATE_MISMATCH", result.RuleId);
            Assert.Equal("Critical", result.Severity);
            Assert.Contains("偏差 > 100倍", result.Title);
        }

        [Fact]
        public void MemoryGrantRule_ShouldDetectExcessiveGrant()
        {
            var rule = new LargeMemoryGrantRule();
            var xml = $@"<ShowPlanXML xmlns=""{ns}""><RelOp NodeId=""0"">
                            <MemoryGrantInfo GrantedMemory=""102400"" MaxUsedMemory=""1024"" />
                         </RelOp></ShowPlanXML>";
            var doc = XDocument.Parse(xml);
            var result = rule.Analyze(doc.Descendants(ns + "RelOp").First(), ns);
            Assert.NotNull(result);
            Assert.Equal("RULE_017_LARGE_MEMORY_GRANT", result.RuleId);
            Assert.Equal("Warning", result.Severity);
            Assert.Contains("内存过度分配", result.Title);
        }

        [Fact]
        public void ResidualPredicateRule_ShouldDetectFunctionWrapped()
        {
            var rule = new ResidualPredicateRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""4"" PhysicalOp=""Index Seek"">
                            <IndexScan>
                                <Predicate>
                                    <ScalarOperator ScalarString=""YEAR([DateCol]) = 2023"" />
                                </Predicate>
                            </IndexScan>
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_007_NON_SARGABLE", result.RuleId);
            Assert.Contains("非 SARGable", result.Title);
            Assert.Contains("YEAR", result.Message);
        }

        [Fact]
        public void SpillDetectionRule_ShouldDetectSevereSpill()
        {
            var rule = new SpillDetectionRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""5"" PhysicalOp=""Hash Match"">
                            <Warnings>
                                <HashSpillDetails SpillLevel=""5"" SpilledPages=""1000"" />
                            </Warnings>
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_008_SPILL_DETECTION", result.RuleId);
            Assert.Equal("Critical", result.Severity);
            Assert.Contains("严重 TempDB 溢出", result.Title);
        }

        [Fact]
        public void ParallelSkewRule_ShouldDetectSkew()
        {
            var rule = new ParallelSkewRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""6"" Parallel=""1"">
                            <RunTimeInformation>
                                <RunTimeCountersPerThread Thread=""1"" ActualRows=""9000"" />
                                <RunTimeCountersPerThread Thread=""2"" ActualRows=""1000"" />
                                <RunTimeCountersPerThread Thread=""3"" ActualRows=""100"" />
                                <RunTimeCountersPerThread Thread=""4"" ActualRows=""50"" />
                            </RunTimeInformation>
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_009_PARALLEL_SKEW", result.RuleId);
            Assert.Equal("Warning", result.Severity);
            Assert.Contains("并行线程分布倾斜", result.Title);
        }

        [Fact]
        public void UdfAndTableVariableRule_ShouldDetectTableVariable()
        {
            var rule = new UdfAndTableVariableRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""7"" PhysicalOp=""Table Valued Function"" EstimateRows=""1"">
                            <RunTimeInformation>
                                <RunTimeCountersPerThread ActualRows=""500"" />
                            </RunTimeInformation>
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_011_UDF_TVF", result.RuleId);
            Assert.Equal("Critical", result.Severity);
            Assert.Contains("UDF / 表变量", result.Title);
            Assert.Contains("偏差 500 倍", result.Message);
        }

        [Fact]
        public void NestedLoopsHighExecRule_ShouldDetectHighExecutions()
        {
            var rule = new NestedLoopsHighExecRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""8"" PhysicalOp=""Nested Loops"">
                            <RunTimeInformation>
                                <RunTimeCountersPerThread ActualExecutions=""50000"" ActualRows=""100000"" />
                            </RunTimeInformation>
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_012_NESTED_LOOPS_HIGH_EXEC", result.RuleId);
            Assert.Equal("Critical", result.Severity);
            Assert.Contains("嵌套循环执行次数过高", result.Title);
        }

        [Fact]
        public void AntiPatternRule_ShouldDetectLeadingWildcard()
        {
            var rule = new AntiPatternRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""9"">
                            <Predicate>
                                <ScalarOperator ScalarString=""[Name] LIKE '%Test'"" />
                            </Predicate>
                         </RelOp>";
            var element = XElement.Parse(xml);

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_013_ANTI_PATTERN", result.RuleId);
            Assert.Contains("前导通配符", result.Title);
        }

        [Fact]
        public void SerialPlanReasonRule_ShouldDetectReason()
        {
            var rule = new SerialPlanReasonRule();
            var xml = $@"<RelOp xmlns=""{ns}"" NodeId=""0"">
                            <QueryPlan NonParallelPlanReason=""MaxDOPSetToOne"" />
                         </RelOp>";
            // Make sure the XML matches what the rule expects: Root -> BatchSequence -> ... or Descendants("QueryPlan")
            var docXml = $@"<ShowPlanXML xmlns=""{ns}"">
                                <BatchSequence>
                                    <Batch>
                                        <Statements>
                                            <StmtSimple>
                                                <QueryPlan NonParallelPlanReason=""MaxDOPSetToOne"">
                                                    <RelOp NodeId=""0""></RelOp>
                                                </QueryPlan>
                                            </StmtSimple>
                                        </Statements>
                                    </Batch>
                                </BatchSequence>
                            </ShowPlanXML>";
            var doc = XDocument.Parse(docXml);
            var element = doc.Descendants(ns + "RelOp").First();

            var result = rule.Analyze(element, ns);

            Assert.NotNull(result);
            Assert.Equal("RULE_014_SERIAL_PLAN_REASON", result.RuleId);
            Assert.Contains("串行执行计划", result.Title);
            Assert.Contains("MAXDOP 被设置为 1", result.Message);
        }
    }
}
