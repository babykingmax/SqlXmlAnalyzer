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
    }
}
