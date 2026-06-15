using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Rules;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Rules
{
    public class SargableIndexRecommendationRuleTests
    {
        [Fact]
        public void Analyze_WithIndexScanAndNonSargable_ReturnsCorrectSuggestions()
        {
            // Arrange
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"" Version=""1.6"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementText=""SELECT Name, Age FROM Users WHERE UserID = 123 AND UPPER(Email) = 'TEST@TEST.COM'"" StatementId=""1"">
          <QueryPlan>
            <RelOp NodeId=""1"" PhysicalOp=""Index Scan"" LogicalOp=""Index Scan"" EstimateRows=""100"">
              <IndexScan>
                <Object Schema=""[dbo]"" Table=""[Users]"" />
              </IndexScan>
              <OutputList>
                <ColumnReference Table=""[Users]"" Column=""Email"" />
              </OutputList>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            var relOp = doc.Descendants(ns + "RelOp").First();

            var rule = new SargableIndexRecommendationRule();

            // Act
            var result = rule.Analyze(relOp, ns);

            // Assert
            result.Should().NotBeNull();
            result!.RuleId.Should().Be("RULE_035_SARGABLE_INDEX_RECOMMENDATION");
            result.Severity.Should().Be("Warning");
            result.Title.Should().Be("智能索引与 T-SQL 关联建议");
            result.Message.Should().Contain("[智能索引推荐]");
            result.Message.Should().Contain("[非 SARGable 表达式警告]");
            result.Message.Should().Contain("Users");
            result.Message.Should().Contain("Email");
        }

        [Fact]
        public void Analyze_RootNode_ReturnsSummary()
        {
            // Arrange
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"" Version=""1.6"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementText=""SELECT Name, Age FROM Users WHERE UserID = 123 AND UPPER(Email) = 'TEST@TEST.COM'"" StatementId=""1"">
          <QueryPlan>
            <RelOp NodeId=""0"" PhysicalOp=""Nested Loops"" LogicalOp=""Inner Join"">
            </RelOp>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            var relOp = doc.Descendants(ns + "RelOp").First();

            var rule = new SargableIndexRecommendationRule();

            // Act
            var result = rule.Analyze(relOp, ns);

            // Assert
            result.Should().NotBeNull();
            result!.NodeId.Should().Be("0");
            result.Title.Should().Be("智能索引与 T-SQL 关联汇总");
            result.Message.Should().Contain("全局智能索引推荐");
            result.Message.Should().Contain("无法自动改写的非 SARGable 表达式");
        }

        [Fact]
        public void Analyze_MultiDocumentIsolation_StateDoesNotLeakAcrossDocuments()
        {
            // Arrange
            string xml1 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"" Version=""1.6"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementText=""SELECT Name FROM Users WHERE UserID = 123"" StatementId=""1"">
          <QueryPlan>
            <RelOp NodeId=""1"" PhysicalOp=""Index Scan"" LogicalOp=""Index Scan"">
              <IndexScan>
                <Object Schema=""[dbo]"" Table=""[Users]"" />
              </IndexScan>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            string xml2 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"" Version=""1.6"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementText=""SELECT Title FROM Products WHERE ProductID = 456"" StatementId=""1"">
          <QueryPlan>
            <RelOp NodeId=""1"" PhysicalOp=""Index Scan"" LogicalOp=""Index Scan"">
              <IndexScan>
                <Object Schema=""[dbo]"" Table=""[Products]"" />
              </IndexScan>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            var doc1 = XDocument.Parse(xml1);
            var doc2 = XDocument.Parse(xml2);
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

            var rule = new SargableIndexRecommendationRule();

            // Act - Analyze first doc
            var relOp1 = doc1.Descendants(ns + "RelOp").First();
            var result1 = rule.Analyze(relOp1, ns);

            // Act - Analyze second doc with same rule instance
            var relOp2 = doc2.Descendants(ns + "RelOp").First();
            var result2 = rule.Analyze(relOp2, ns);

            // Assert
            result1.Should().NotBeNull();
            result1!.Message.Should().Contain("Users");
            result1!.Message.Should().NotContain("Products");

            result2.Should().NotBeNull();
            result2!.Message.Should().Contain("Products");
            result2!.Message.Should().NotContain("Users");
        }

        [Fact]
        public void Analyze_WithMultiStatementPlan_IsolatesRecommendationsCorrectly()
        {
            // Arrange
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"" Version=""1.6"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementText=""SELECT Name FROM Users WHERE UserID = 123"" StatementId=""1"">
          <QueryPlan>
            <RelOp NodeId=""1"" PhysicalOp=""Index Scan"" LogicalOp=""Index Scan"">
              <IndexScan>
                <Object Schema=""[dbo]"" Table=""[Users]"" />
              </IndexScan>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
        <StmtSimple StatementText=""SELECT Title FROM Products WHERE ProductID = 456"" StatementId=""2"">
          <QueryPlan>
            <RelOp NodeId=""2"" PhysicalOp=""Index Scan"" LogicalOp=""Index Scan"">
              <IndexScan>
                <Object Schema=""[dbo]"" Table=""[Products]"" />
              </IndexScan>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            var rule = new SargableIndexRecommendationRule();

            var relOps = doc.Descendants(ns + "RelOp").ToList();
            var relOp1 = relOps.First(r => r.Attribute("NodeId")?.Value == "1");
            var relOp2 = relOps.First(r => r.Attribute("NodeId")?.Value == "2");

            // Act
            var result1 = rule.Analyze(relOp1, ns);
            var result2 = rule.Analyze(relOp2, ns);

            // Assert
            result1.Should().NotBeNull();
            result1!.Message.Should().Contain("Users");
            result1!.Message.Should().NotContain("Products");

            result2.Should().NotBeNull();
            result2!.Message.Should().Contain("Products");
            result2!.Message.Should().NotContain("Users");
        }
    }
}
