using System.Collections.Generic;
using System.Xml.Linq;
using Xunit;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Scoring;

namespace SqlXmlAnalyzer.Tests.Scoring
{
    public class IndexScoringCalculatorTests
    {
        [Fact]
        public void CalculateScore_EmptyColumns_ShouldReturnLowScore()
        {
            var suggestion = new MissingIndexSuggestion();
            IndexScoringCalculator.CalculateScore(suggestion, null!, null!);

            suggestion.Score.Should().BeGreaterThanOrEqualTo(0);
            suggestion.Score.Should().BeLessThan(50);
        }

        [Fact]
        public void CalculateScore_HighCoverageAndSeekability_ShouldReturnHighScore()
        {
            var suggestion = new MissingIndexSuggestion
            {
                KeyColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "Col1", Usage = "EQUALITY" },
                    new IndexColumn { Name = "Col2", Usage = "EQUALITY" },
                    new IndexColumn { Name = "Col3", Usage = "INEQUALITY" }
                },
                IncludeColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "Col4", Usage = "INCLUDE" }
                }
            };

            IndexScoringCalculator.CalculateScore(suggestion, null!, null!);

            suggestion.Score.Should().BeGreaterThan(75);
        }

        [Fact]
        public void CalculateScore_OnlyIncludes_ShouldReturnLowerScoreThanKey()
        {
            var suggestion = new MissingIndexSuggestion
            {
                IncludeColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "Col1", Usage = "INCLUDE" },
                    new IndexColumn { Name = "Col2", Usage = "INCLUDE" }
                }
            };

            IndexScoringCalculator.CalculateScore(suggestion, null!, null!);
            suggestion.Score.Should().BeLessThan(70);
        }

        [Fact]
        public void CalculateScore_WithComplexPlan_ShouldCalculateBasedOnPredicateMatches()
        {
            var xml = @"
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple>
          <QueryPlan>
            <RelOp PhysicalOp=""Index Scan"" LogicalOp=""Index Scan"">
              <OutputList>
                <ColumnReference Column=""Col1"" />
                <ColumnReference Column=""Col2"" />
                <ColumnReference Column=""Col3"" />
              </OutputList>
              <IndexScan>
                <Object Table=""[MyTable]"" />
                <Predicate>
                  <ScalarOperator ScalarString=""[MyTable].[Col1] = [@val1] AND [MyTable].[Col2] &gt;= [@val2]"" />
                </Predicate>
              </IndexScan>
            </RelOp>
            <RelOp PhysicalOp=""Sort"" LogicalOp=""Sort"">
              <Sort>
                <OrderBy>
                  <OrderByColumn>
                    <ColumnReference Column=""Col3"" />
                  </OrderByColumn>
                </OrderBy>
              </Sort>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            var planDoc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

            var suggestion = new MissingIndexSuggestion
            {
                Table = "[MyTable]",
                KeyColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "Col1", Usage = "EQUALITY" },   // Matches equality: +30
                    new IndexColumn { Name = "Col2", Usage = "INEQUALITY" }, // Matches inequality (first non-equality): +15
                    new IndexColumn { Name = "Col3", Usage = "INEQUALITY" }  // Matches ORDER BY, but blocked by range on Col2: +0
                },
                IncludeColumns = new List<IndexColumn>()
            };

            IndexScoringCalculator.CalculateScore(suggestion, planDoc, ns);

            // seq = 30 (Col1)
            // sineq = 15 (Col2)
            // sorder = 0 (Col3 is after Col1, but blocked by range predicate on Col2)
            // scover = 40 (OutputList has Col1, Col2, Col3 => fully covered: 40)
            // penalty = 0
            // Total = 30 + 15 + 0 + 40 - 0 = 85
            suggestion.Score.Should().Be(85);
        }

        [Fact]
        public void CalculateScore_WithComplexPlan_NoBlockingRange_ShouldScore100()
        {
            var xml = @"
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple>
          <QueryPlan>
            <RelOp PhysicalOp=""Index Scan"" LogicalOp=""Index Scan"">
              <OutputList>
                <ColumnReference Column=""Col1"" />
                <ColumnReference Column=""Col2"" />
                <ColumnReference Column=""Col3"" />
              </OutputList>
              <IndexScan>
                <Object Table=""[MyTable]"" />
                <Predicate>
                  <ScalarOperator ScalarString=""[MyTable].[Col1] = [@val1] AND [MyTable].[Col2] = [@val2]"" />
                </Predicate>
              </IndexScan>
            </RelOp>
            <RelOp PhysicalOp=""Sort"" LogicalOp=""Sort"">
              <Sort>
                <OrderBy>
                  <OrderByColumn>
                    <ColumnReference Column=""Col3"" />
                  </OrderByColumn>
                </OrderBy>
              </Sort>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            var planDoc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

            var suggestion = new MissingIndexSuggestion
            {
                Table = "[MyTable]",
                KeyColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "Col1", Usage = "EQUALITY" }, // Matches equality: +30
                    new IndexColumn { Name = "Col2", Usage = "EQUALITY" }, // Matches equality: +30
                    new IndexColumn { Name = "Col3", Usage = "INEQUALITY" } // Matches ORDER BY (Col3 is not blocked by range): +15
                },
                IncludeColumns = new List<IndexColumn>()
            };

            IndexScoringCalculator.CalculateScore(suggestion, planDoc, ns);

            // seq = 60 (Col1 + Col2)
            // sineq = 0 (no range predicate)
            // sorder = 15 (Col3 is sort column, follows equality cols Col1,Col2 and is not blocked)
            // scover = 40 (OutputList Col1,Col2,Col3 fully covered)
            // penalty = 0
            // Total = 60 + 0 + 15 + 40 - 0 = 115 -> normalized to 100
            suggestion.Score.Should().Be(100);
        }
    }
}
