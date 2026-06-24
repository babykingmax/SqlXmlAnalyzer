using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Tests.Utilities;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class PlanDiagnosticAnalyzerTests
    {
        private readonly RuleEngine _ruleEngine;
        private readonly XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        public PlanDiagnosticAnalyzerTests()
        {
            _ruleEngine = new RuleEngine();
            _ruleEngine.RegisterDefaultRules();
        }

        [Fact]
        public void DetectImplicitConversion_ShouldReturnWarning()
        {
            // Arrange
            string xmlContent = EmbeddedResourceHelper.GetResourceContent("plan_implicit_conversion.sqlplan");
            var doc = XDocument.Parse(xmlContent);
            var rootRelOp = doc.Descendants(ns + "RelOp").First();

            // Act
            var results = _ruleEngine.AnalyzeNode(rootRelOp, ns);
            var convResult = results.FirstOrDefault(r => r.RuleId == "RULE_001_IMPLICIT_CONV");

            // Assert
            convResult.Should().NotBeNull();
            convResult!.Message.Should().Contain("CONVERT_IMPLICIT");
            convResult.Severity.Should().Be("Critical"); // It's an Index Scan, so should be Critical
        }

        [Fact]
        public void DetectMissingIndex_ShouldReturnSuggestion()
        {
            // Arrange
            string xmlContent = EmbeddedResourceHelper.GetResourceContent("plan_missing_index.sqlplan");
            var doc = XDocument.Parse(xmlContent);

            // Act
            // Test text report
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            // Test object extraction
            var suggestions = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, ns);

            // Assert Text Report
            report.Should().Contain("Missing Indexes");
            report.Should().Contain("[dbo].[Orders]");
            report.Should().Contain("CREATE NONCLUSTERED INDEX");

            // Assert Object Extraction
            suggestions.Should().NotBeEmpty();
            var mi = suggestions.First();
            mi.Table.Should().Be("[Orders]");
            mi.Schema.Should().Be("[dbo]");
            mi.KeyColumns.Should().NotBeEmpty();
            mi.Score.Should().BeGreaterThan(0);
            mi.CreateIndexStatement.Should().Contain("CREATE NONCLUSTERED INDEX");
        }

        [Fact]
        public void DetectNoWarnings_OnCleanPlan_ReturnsEmpty()
        {
            // Arrange
            string xmlContent = EmbeddedResourceHelper.GetResourceContent("plan_clean.sqlplan");
            var doc = XDocument.Parse(xmlContent);
            var rootRelOp = doc.Descendants(ns + "RelOp").First();

            // Act
            var ruleResults = _ruleEngine.AnalyzeNode(rootRelOp, ns);
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            // Assert
            ruleResults.Should().BeEmpty();
            report.Should().NotContain("CONVERT_IMPLICIT");
            report.Should().NotContain("Missing Indexes");
        }

        [Fact]
        public void DetectKeyLookup_ShouldReturnWarning()
        {
            // Arrange
            string xmlContent = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                                    <RelOp NodeId=""0"" PhysicalOp=""Key Lookup"" LogicalOp=""Key Lookup"">
                                      <IndexScan>
                                        <Object Database=""[TestDB]"" Schema=""[dbo]"" Table=""[Users]"" Index=""[PK_Users]"" />
                                      </IndexScan>
                                    </RelOp>
                                  </ShowPlanXML>";
            var doc = XDocument.Parse(xmlContent);
            var rootRelOp = doc.Descendants(ns + "RelOp").First();

            // Act
            var results = _ruleEngine.AnalyzeNode(rootRelOp, ns);
            var keyLookupResult = results.FirstOrDefault(r => r.RuleId == "RULE_002_KEY_LOOKUP");

            // Assert
            keyLookupResult.Should().NotBeNull();
            keyLookupResult!.Message.Should().Contain("Key Lookup");
            keyLookupResult.Message.Should().Contain("Users.PK_Users");
            keyLookupResult.Severity.Should().Be("Warning");
        }

        [Fact]
        public void DetectParameterSniffing_ShouldReturnWarning()
        {
            // Arrange
            string xmlContent = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                                    <RelOp NodeId=""0"" PhysicalOp=""Nested Loops"">
                                      <ParameterList>
                                        <ColumnReference Column=""@p1"" ParameterCompiledValue=""(1)"" ParameterRuntimeValue=""(2)"" />
                                      </ParameterList>
                                    </RelOp>
                                  </ShowPlanXML>";
            var doc = XDocument.Parse(xmlContent);
            var rootRelOp = doc.Descendants(ns + "RelOp").First();

            // Act
            var results = _ruleEngine.AnalyzeNode(rootRelOp, ns);
            var paramResult = results.FirstOrDefault(r => r.RuleId == "RULE_003_PARAM_SNIFFING");

            // Assert
            paramResult.Should().NotBeNull();
            paramResult!.Message.Should().Contain("@p1");
            paramResult.Message.Should().Contain("编译值: (1)");
            paramResult.Message.Should().Contain("运行值: (2)");
            // Expect Info because the ratio is 1 (no ActualRows provided, defaults to Info)
            paramResult.Severity.Should().Be("Info");
        }

        [Fact]
        public void GenerateDiagnosticReport_WithMemoryGrant_ShouldReturnWarning()
        {
            string xmlContent = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                                    <BatchSequence><Batch><Statements><StmtSimple>
                                    <QueryPlan>
                                        <MemoryGrantInfo GrantedMemory=""20480"" MaxUsedMemory=""1024"" />
                                        <RelOp NodeId=""0"" PhysicalOp=""Table Scan"">
                                           <TableScan><Object Database=""[DB]"" Schema=""[dbo]"" Table=""[T1]"" /></TableScan>
                                        </RelOp>
                                    </QueryPlan>
                                    </StmtSimple></Statements></Batch></BatchSequence>
                                  </ShowPlanXML>";
            var doc = XDocument.Parse(xmlContent);
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            report.Should().Contain("内存预估过度");
            // report.Should().Contain("Table Scan"); // No EstimateRows so RelOp diagnostic skipped
        }

        [Fact]
        public void GenerateDiagnosticReport_WithImplicitConversion_ShouldReturnWarning()
        {
            string xmlContent = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                                    <BatchSequence><Batch><Statements><StmtSimple>
                                    <QueryPlan>
                                        <PlanAffectingConvert Expression=""CONVERT_IMPLICIT(INT, [X])"" />
                                        <RelOp NodeId=""0"" PhysicalOp=""Compute Scalar"">
                                            <ComputeScalar>
                                                <ScalarOperator ScalarString=""CONVERT_IMPLICIT(VARCHAR, [Y])"" />
                                            </ComputeScalar>
                                        </RelOp>
                                    </QueryPlan>
                                    </StmtSimple></Statements></Batch></BatchSequence>
                                  </ShowPlanXML>";
            var doc = XDocument.Parse(xmlContent);
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            report.Should().Contain("隐式转换风险");
            report.Should().Contain("CONVERT_IMPLICIT(INT, [X])");
            report.Should().Contain("CONVERT_IMPLICIT(VARCHAR, [Y])");
        }

        [Fact]
        public void GenerateDiagnosticReport_WithSpoolAndSort_ShouldReturnWarning()
        {
            string xmlContent = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                                    <BatchSequence><Batch><Statements><StmtSimple>
                                    <QueryPlan>
                                        <RelOp NodeId=""0"" PhysicalOp=""Table Spool"" EstimateRows=""100"" EstimatedTotalSubtreeCost=""50""></RelOp>
                                        <RelOp NodeId=""1"" PhysicalOp=""Sort"" EstimateRows=""100"" EstimatedTotalSubtreeCost=""10""></RelOp>
                                    </QueryPlan>
                                    </StmtSimple></Statements></Batch></BatchSequence>
                                  </ShowPlanXML>";
            var doc = XDocument.Parse(xmlContent);
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            report.Should().Contain("Spool");
            report.Should().Contain("Sort");
        }

        [Fact]
        public void GenerateDiagnosticReport_WithCardinalityEstimationError_ShouldReturnWarning()
        {
            string xmlContent = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                                    <BatchSequence><Batch><Statements><StmtSimple>
                                    <QueryPlan>
                                        <!-- UDF Bomb -->
                                        <RelOp NodeId=""7"" PhysicalOp=""Table Valued Function"" LogicalOp=""Table Valued Function"" EstimateRows=""10"">
                                            <RunTimeInformation>
                                                <RunTimeCountersPerThread Thread=""0"" ActualRows=""2000"" />
                                            </RunTimeInformation>
                                        </RelOp>
                                        
                                        <!-- Card error with AND -->
                                        <RelOp NodeId=""8"" PhysicalOp=""Table Scan"" EstimateRows=""1"">
                                            <Predicate><ScalarOperator ScalarString=""[A] = 1 AND [B] = 2"" /></Predicate>
                                            <RunTimeInformation><RunTimeCountersPerThread Thread=""0"" ActualRows=""2000"" /></RunTimeInformation>
                                        </RelOp>

                                        <!-- Key Lookup -->
                                        <RelOp NodeId=""9"" PhysicalOp=""Key Lookup"">
                                            <IndexScan>
                                                <Object Database=""[A]"" Schema=""[dbo]"" Table=""[T]"" />
                                            </IndexScan>
                                        </RelOp>
                                        
                                        <!-- Card error with Function -->
                                        <RelOp NodeId=""10"" PhysicalOp=""Table Scan"" EstimateRows=""1"">
                                            <Predicate><ScalarOperator ScalarString=""UPPER([A]) = 'A'"" /></Predicate>
                                            <RunTimeInformation><RunTimeCountersPerThread Thread=""0"" ActualRows=""2000"" /></RunTimeInformation>
                                        </RelOp>
                                        
                                        <!-- Index Seek without RunTimeInformation -->
                                        <RelOp NodeId=""11"" PhysicalOp=""Index Seek"">
                                            <IndexSeek>
                                                <SeekPredicates><SeekPredicateNew><ScalarOperator ScalarString=""[Id]=1""/></SeekPredicateNew></SeekPredicates>
                                            </IndexSeek>
                                            <Predicate><ScalarOperator ScalarString=""[B]=2""/></Predicate>
                                        </RelOp>
                                    </QueryPlan>
                                    </StmtSimple></Statements></Batch></BatchSequence>
                                  </ShowPlanXML>";
            var doc = XDocument.Parse(xmlContent);
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            report.Should().Contain("基数估计偏离");
        }



        [Fact]
        public void GenerateDiagnosticReport_WaitStats_ShouldReturnWarning()
        {
            string xmlContent = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                                    <BatchSequence><Batch><Statements>
                                    <StmtSimple StatementOptmEarlyAbortReason=""TimeOut"" StatementOptmLevel=""FULL"" StatementSubTreeCost=""60.5"">
                                    <QueryPlan>
                                        <WaitStats>
                                            <Wait WaitType=""CXPACKET"" WaitTimeMs=""200"" />
                                            <Wait WaitType=""RESOURCE_SEMAPHORE"" WaitTimeMs=""1200"" />
                                        </WaitStats>
                                        <QueryTimeStats CompileTime=""600"" CompileCPU=""550"" />
                                    </QueryPlan>
                                    </StmtSimple>
                                    </Statements></Batch></BatchSequence>
                                  </ShowPlanXML>";
            var doc = XDocument.Parse(xmlContent);
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            report.Should().Contain("发现显著资源等待");
            report.Should().Contain("CXPACKET");
            report.Should().Contain("内存准入排队");
            report.Should().Contain("SQL 优化器因 [TimeOut] 提前中止");
            report.Should().Contain("重编译高开销");
            report.Should().Contain("复杂计划编译");
        }
        [Fact]
        public void GenerateDiagnosticReport_WithAdvancedDiagnostics_ShouldReturnWarnings()
        {
            string xmlContent = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                                    <BatchSequence><Batch><Statements><StmtSimple StatementText=""SELECT (SELECT 1), (SELECT 2) FROM T"">
                                    <QueryPlan>
                                        <ParameterList>
                                            <ColumnReference Column=""@p1"" ParameterCompiledValue=""1"" ParameterRuntimeValue=""1000"" />
                                        </ParameterList>
                                        <ScalarOperator ScalarString=""subquery"" />
                                        <ScalarOperator ScalarString=""subquery"" />
                                        <ScalarOperator ScalarString=""subquery"" />
                                        <!-- Nested Loops Inequality -->
                                        <RelOp NodeId=""0"" PhysicalOp=""Nested Loops"">
                                            <NestedLoops>
                                                <RelOp NodeId=""1"" PhysicalOp=""Index Seek"">
                                                    <IndexSeek>
                                                        <SeekPredicates>
                                                            <SeekPredicateNew>
                                                                <SeekKeys>
                                                                    <Prefix ScanType=""GT"">
                                                                        <RangeColumns><ColumnReference Column=""[Id]""/></RangeColumns>
                                                                    </Prefix>
                                                                </SeekKeys>
                                                                <ScalarOperator ScalarString=""[Id] &gt; 1""/>
                                                            </SeekPredicateNew>
                                                        </SeekPredicates>
                                                    </IndexSeek>
                                                </RelOp>
                                                <RelOp NodeId=""2"" PhysicalOp=""Table Scan""></RelOp>
                                            </NestedLoops>
                                        </RelOp>
                                        <RelOp NodeId=""3"" PhysicalOp=""Nested Loops""><NestedLoops></NestedLoops></RelOp>
                                        
                                        <!-- Thread Skew -->
                                        <RelOp NodeId=""4"" PhysicalOp=""Sort"">
                                            <Warnings><SpillToTempDb/></Warnings>
                                            <RunTimeInformation>
                                                <RunTimeCountersPerThread Thread=""1"" ActualRows=""10"" />
                                                <RunTimeCountersPerThread Thread=""2"" ActualRows=""10"" />
                                                <RunTimeCountersPerThread Thread=""3"" ActualRows=""2000"" />
                                            </RunTimeInformation>
                                        </RelOp>
                                        
                                        <!-- Wide Table Scan -->
                                        <RelOp NodeId=""5"" PhysicalOp=""Table Scan"" EstimateRows=""2000"" TableCardinality=""2000"">
                                           <TableScan><Object Database=""[DB]"" Schema=""[dbo]"" Table=""[T]""/></TableScan>
                                        </RelOp>
                                        
                                        <!-- Residual Predicate -->
                                        <RelOp NodeId=""6"" PhysicalOp=""Index Seek"">
                                            <IndexSeek>
                                                <SeekPredicates><SeekPredicateNew><ScalarOperator ScalarString=""[Id]=1""/></SeekPredicateNew></SeekPredicates>
                                            </IndexSeek>
                                            <Predicate><ScalarOperator ScalarString=""[B]=2""/></Predicate>
                                            <RunTimeInformation>
                                                <RunTimeCountersPerThread Thread=""0"" ActualRows=""10"" ActualRowsRead=""5000"" />
                                            </RunTimeInformation>
                                        </RelOp>
                                        
                                        <!-- UDF Bomb -->
                                        <RelOp NodeId=""7"" PhysicalOp=""Table Valued Function"" LogicalOp=""Table Valued Function"" EstimateRows=""10"">
                                            <RunTimeInformation>
                                                <RunTimeCountersPerThread Thread=""0"" ActualRows=""2000"" />
                                            </RunTimeInformation>
                                        </RelOp>
                                        
                                    </QueryPlan>
                                    </StmtSimple></Statements></Batch></BatchSequence>
                                  </ShowPlanXML>";
            var doc = XDocument.Parse(xmlContent);
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            report.Should().Contain("嵌套循环 + 不等式查找");
            report.Should().Contain("线程倾斜");
            report.Should().Contain("宽表扫描");
            report.Should().Contain("残差谓词");
            report.Should().Contain("表变量性能警告");
        }
    }
}
