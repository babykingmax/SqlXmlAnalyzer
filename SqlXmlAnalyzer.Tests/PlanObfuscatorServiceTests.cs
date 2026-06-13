using System;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using Xunit;
using System.Linq;

namespace SqlXmlAnalyzer.Tests
{
    public class PlanObfuscatorServiceTests
    {
        [Fact]
        public void ObfuscatePlan_ShouldThrow_WhenPlanIsNull()
        {
            Action act = () => PlanObfuscatorService.ObfuscatePlan(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void ObfuscatePlan_ShouldObfuscateSensitiveFields()
        {
            // Arrange
            var doc = XDocument.Parse(@"
                <ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                    <BatchSequence>
                        <Batch>
                            <Statements>
                                <StmtSimple StatementText=""SELECT Salary, Name FROM dbo.Employees WHERE Salary > 50000"">
                                    <QueryPlan>
                                        <RelOp NodeId=""1"" PhysicalOp=""Clustered Index Scan"" EstimateRows=""10"">
                                            <IndexScan Database=""[MyProductionDb]"" Schema=""[dbo]"" Table=""[Employees]"" Index=""[PK_Employees]"">
                                                <DefinedValues>
                                                    <DefinedValue>
                                                        <ColumnReference Database=""[MyProductionDb]"" Schema=""[dbo]"" Table=""[Employees]"" Column=""Salary"" />
                                                    </DefinedValue>
                                                </DefinedValues>
                                            </IndexScan>
                                        </RelOp>
                                        <ParameterList>
                                            <ColumnReference Column=""@SalaryParam"" ParameterCompiledValue=""(50000)"" ParameterRuntimeValue=""(60000)"" />
                                        </ParameterList>
                                        <ScalarOperator ScalarString=""[MyProductionDb].[dbo].[Employees].[Salary] > 50000"">
                                            <Identifier />
                                        </ScalarOperator>
                                    </QueryPlan>
                                </StmtSimple>
                            </Statements>
                        </Batch>
                    </BatchSequence>
                </ShowPlanXML>");

            // Act
            var obfuscated = PlanObfuscatorService.ObfuscatePlan(doc);

            // Assert
            var root = obfuscated.Root;
            root.Should().NotBeNull();

            // StatementText should be obfuscated
            var stmt = root!.Descendants().First(e => e.Name.LocalName == "StmtSimple");
            stmt.Attribute("StatementText")!.Value.Should().Contain("脱敏");

            // Database, Table, Schema, Column, Index should be obfuscated
            var scan = root.Descendants().First(e => e.Name.LocalName == "IndexScan");
            scan.Attribute("Database")!.Value.Should().NotContain("MyProductionDb");
            scan.Attribute("Table")!.Value.Should().NotContain("Employees");
            scan.Attribute("Index")!.Value.Should().NotContain("PK_Employees");
            scan.Attribute("Schema")!.Value.Should().Be("[dbo]"); // dbo is excluded from mask in service

            var colRef = root.Descendants().First(e => e.Name.LocalName == "ColumnReference" && e.Attribute("Column") != null && e.Attribute("Column")!.Value != "@SalaryParam");
            colRef.Attribute("Column")!.Value.Should().NotContain("Salary");

            // ParameterCompiledValue and ParameterRuntimeValue should be masked
            var paramRef = root.Descendants().First(e => e.Name.LocalName == "ColumnReference" && e.Attribute("Column")!.Value == "@SalaryParam");
            paramRef.Attribute("ParameterCompiledValue")!.Value.Should().Be("[MASKED_PARAM_VAL]");
            paramRef.Attribute("ParameterRuntimeValue")!.Value.Should().Be("[MASKED_PARAM_VAL]");

            // ScalarString should be masked
            var scalarOp = root.Descendants().First(e => e.Name.LocalName == "ScalarOperator");
            scalarOp.Attribute("ScalarString")!.Value.Should().Be("[Masked Formula / Predicate Expression]");
        }
    }
}
