using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphMissingIndexAssociationServiceTests
    {
        private readonly PlanGraphMissingIndexAssociationService _service = new();

        [Fact]
        public void MatchSuggestions_MatchesTablesIgnoringBracketsAndCase()
        {
            MissingIndexSuggestion suggestion = new()
            {
                Table = "[Orders]"
            };

            IReadOnlyList<MissingIndexSuggestion?> result =
                _service.MatchSuggestions(
                    new[] { new PlanGraphMissingIndexNodeInfo("orders") },
                    new[] { suggestion });

            result.Should().ContainSingle()
                .Which.Should().BeSameAs(suggestion);
        }

        [Fact]
        public void MatchSuggestions_WhenNodeTableIsEmpty_ReturnsNull()
        {
            IReadOnlyList<MissingIndexSuggestion?> result =
                _service.MatchSuggestions(
                    new[] { new PlanGraphMissingIndexNodeInfo(string.Empty) },
                    new[] { new MissingIndexSuggestion { Table = "[Orders]" } });

            result.Should().ContainSingle()
                .Which.Should().BeNull();
        }

        [Fact]
        public void MatchSuggestions_WhenNoSuggestionMatches_ReturnsNull()
        {
            IReadOnlyList<MissingIndexSuggestion?> result =
                _service.MatchSuggestions(
                    new[] { new PlanGraphMissingIndexNodeInfo("[Customers]") },
                    new[] { new MissingIndexSuggestion { Table = "[Orders]" } });

            result.Should().ContainSingle()
                .Which.Should().BeNull();
        }

        [Fact]
        public void MatchSuggestions_PreservesNodeOrderAndFirstMatch()
        {
            MissingIndexSuggestion firstOrdersSuggestion = new()
            {
                Table = "[Orders]"
            };
            MissingIndexSuggestion secondOrdersSuggestion = new()
            {
                Table = "[Orders]"
            };
            MissingIndexSuggestion customerSuggestion = new()
            {
                Table = "[Customers]"
            };

            IReadOnlyList<MissingIndexSuggestion?> result =
                _service.MatchSuggestions(
                    new[]
                    {
                        new PlanGraphMissingIndexNodeInfo("[Customers]"),
                        new PlanGraphMissingIndexNodeInfo("[Orders]")
                    },
                    new[]
                    {
                        firstOrdersSuggestion,
                        secondOrdersSuggestion,
                        customerSuggestion
                    });

            result.Should().Equal(customerSuggestion, firstOrdersSuggestion);
        }
    }
}
