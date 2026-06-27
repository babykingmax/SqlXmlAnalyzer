using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphConnectionHighlightServiceTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ShouldHighlight_WhenNoNodeIsSelected_ReturnsTrueForEveryConnection(
            string? selectedNodeId)
        {
            var service = new PlanGraphConnectionHighlightService();

            bool result = service.ShouldHighlight(selectedNodeId, "child", "parent");

            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldHighlight_WhenSelectedNodeIsSource_ReturnsTrue()
        {
            var service = new PlanGraphConnectionHighlightService();

            bool result = service.ShouldHighlight("child", "child", "parent");

            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldHighlight_WhenSelectedNodeIsTarget_ReturnsTrue()
        {
            var service = new PlanGraphConnectionHighlightService();

            bool result = service.ShouldHighlight("parent", "child", "parent");

            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldHighlight_WhenSelectedNodeIsUnrelated_ReturnsFalse()
        {
            var service = new PlanGraphConnectionHighlightService();

            bool result = service.ShouldHighlight("other", "child", "parent");

            result.Should().BeFalse();
        }
    }
}
