using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphConnectionGeometryServiceTests
    {
        private readonly PlanGraphConnectionGeometryService _service = new();

        [Theory]
        [InlineData(PlanGraphConnectionLayout.Horizontal, 180)]
        [InlineData(PlanGraphConnectionLayout.Vertical, -90)]
        public void GetArrowAngle_ReturnsLayoutAngle(
            PlanGraphConnectionLayout layout,
            double expected)
        {
            double result = _service.GetArrowAngle(layout);

            result.Should().Be(expected);
        }

        [Fact]
        public void CalculateSourceLocation_WhenHorizontalSourceIsRightOfTarget_UsesSourceLeftEdge()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateSourceLocation(
                    new PlanGraphConnectionGeometryNode(300, 100),
                    new PlanGraphConnectionGeometryNode(20, 50),
                    PlanGraphConnectionLayout.Horizontal);

            result.Should().Be(new PlanGraphConnectionPoint(300, 135));
        }

        [Fact]
        public void CalculateTargetLocation_WhenHorizontalSourceIsRightOfTarget_UsesTargetRightEdge()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateTargetLocation(
                    new PlanGraphConnectionGeometryNode(300, 100),
                    new PlanGraphConnectionGeometryNode(20, 50),
                    PlanGraphConnectionLayout.Horizontal);

            result.Should().Be(new PlanGraphConnectionPoint(248, 85));
        }

        [Fact]
        public void CalculateSourceLocation_WhenHorizontalSourceIsLeftOfTarget_UsesSourceRightEdge()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateSourceLocation(
                    new PlanGraphConnectionGeometryNode(20, 50),
                    new PlanGraphConnectionGeometryNode(300, 100),
                    PlanGraphConnectionLayout.Horizontal);

            result.Should().Be(new PlanGraphConnectionPoint(248, 85));
        }

        [Fact]
        public void CalculateTargetLocation_WhenHorizontalSourceIsLeftOfTarget_UsesTargetLeftEdge()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateTargetLocation(
                    new PlanGraphConnectionGeometryNode(20, 50),
                    new PlanGraphConnectionGeometryNode(300, 100),
                    PlanGraphConnectionLayout.Horizontal);

            result.Should().Be(new PlanGraphConnectionPoint(300, 135));
        }

        [Fact]
        public void CalculateSourceLocation_WhenVerticalSourceIsBelowTarget_UsesSourceTopEdge()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateSourceLocation(
                    new PlanGraphConnectionGeometryNode(100, 300),
                    new PlanGraphConnectionGeometryNode(50, 20),
                    PlanGraphConnectionLayout.Vertical);

            result.Should().Be(new PlanGraphConnectionPoint(215, 300));
        }

        [Fact]
        public void CalculateTargetLocation_WhenVerticalSourceIsBelowTarget_UsesTargetBottomEdge()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateTargetLocation(
                    new PlanGraphConnectionGeometryNode(100, 300),
                    new PlanGraphConnectionGeometryNode(50, 20),
                    PlanGraphConnectionLayout.Vertical);

            result.Should().Be(new PlanGraphConnectionPoint(165, 90));
        }

        [Fact]
        public void CalculateSourceLocation_WhenVerticalSourceIsAboveTarget_UsesSourceBottomEdge()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateSourceLocation(
                    new PlanGraphConnectionGeometryNode(50, 20),
                    new PlanGraphConnectionGeometryNode(100, 300),
                    PlanGraphConnectionLayout.Vertical);

            result.Should().Be(new PlanGraphConnectionPoint(165, 90));
        }

        [Fact]
        public void CalculateTargetLocation_WhenVerticalSourceIsAboveTarget_UsesTargetTopEdge()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateTargetLocation(
                    new PlanGraphConnectionGeometryNode(50, 20),
                    new PlanGraphConnectionGeometryNode(100, 300),
                    PlanGraphConnectionLayout.Vertical);

            result.Should().Be(new PlanGraphConnectionPoint(215, 300));
        }

        [Fact]
        public void CalculateSourceLocation_WhenSourceIsMissing_ReturnsOrigin()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateSourceLocation(
                    null,
                    new PlanGraphConnectionGeometryNode(100, 300),
                    PlanGraphConnectionLayout.Horizontal);

            result.Should().Be(new PlanGraphConnectionPoint(0, 0));
        }

        [Fact]
        public void CalculateTargetLocation_WhenTargetIsMissing_ReturnsOrigin()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateTargetLocation(
                    new PlanGraphConnectionGeometryNode(100, 300),
                    null,
                    PlanGraphConnectionLayout.Horizontal);

            result.Should().Be(new PlanGraphConnectionPoint(0, 0));
        }

        [Fact]
        public void CalculateLabelLocation_UsesCurrentLabelWidthEstimate()
        {
            PlanGraphConnectionPoint result =
                _service.CalculateLabelLocation(
                    new PlanGraphConnectionPoint(100, 20),
                    new PlanGraphConnectionPoint(200, 60),
                    "12345");

            result.X.Should().BeApproximately(133.0, 0.0001);
            result.Y.Should().Be(32);
        }
    }
}
