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
            // Note: Missing Index is currently handled by PlanDiagnosticAnalyzer text report in our architecture
            string report = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);

            // Assert
            report.Should().Contain("Missing Indexes");
            report.Should().Contain("[dbo].[Orders]");
            report.Should().Contain("CREATE NONCLUSTERED INDEX");
            report.Should().Contain("(CustomerID)");
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
            paramResult.Message.Should().Contain("Compiled: (1)");
            paramResult.Message.Should().Contain("Runtime: (2)");
            paramResult.Severity.Should().Be("Warning");
        }
    }
}
