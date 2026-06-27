using System;
using System.Collections.Generic;
using System.Windows;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockGraphGeometryServiceTests
    {
        [Fact]
        public void CalculateConnectionPoints_FromProcessToResource_UsesNodeEdges()
        {
            var service = new DeadlockGraphGeometryService();
            var positions = new Dictionary<string, Point>
            {
                ["process1"] = new(80, 150),
                ["res_1"] = new(400, 150)
            };

            DeadlockConnectionPoints points = service.CalculateConnectionPoints(
                positions,
                "process1",
                "res_1");

            points.From.X.Should().BeApproximately(302.9929, 0.0001);
            points.From.Y.Should().BeApproximately(187.2074, 0.0001);
            points.To.X.Should().BeApproximately(397.0071, 0.0001);
            points.To.Y.Should().BeApproximately(180.7236, 0.0001);
        }

        [Fact]
        public void CalculateConnectionPoints_FromResourceToProcess_UsesResourceSourceSize()
        {
            var service = new DeadlockGraphGeometryService();
            var positions = new Dictionary<string, Point>
            {
                ["res_1"] = new(100, 100),
                ["process1"] = new(400, 120)
            };

            DeadlockConnectionPoints points = service.CalculateConnectionPoints(
                positions,
                "res_1",
                "process1");

            points.From.X.Should().BeGreaterThan(260);
            points.From.Y.Should().BeGreaterThan(120);
            points.To.X.Should().BeLessThan(400);
            points.To.Y.Should().BeGreaterThan(130);
        }

        [Fact]
        public void CalculateConnectionPoints_WhenNodePositionIsMissing_UsesFallbackPositions()
        {
            var service = new DeadlockGraphGeometryService();

            DeadlockConnectionPoints points = service.CalculateConnectionPoints(
                new Dictionary<string, Point>(),
                "process1",
                "res_1");

            points.From.X.Should().BeApproximately(302.9929, 0.0001);
            points.To.X.Should().BeApproximately(397.0071, 0.0001);
        }

        [Fact]
        public void CalculateArrowHead_ReturnsTipLeftAndRightPoints()
        {
            var service = new DeadlockGraphGeometryService();

            DeadlockArrowHeadPoints points = service.CalculateArrowHead(
                new Point(100, 0),
                new Point(0, 0));

            points.Tip.Should().Be(new Point(100, 0));
            points.Left.Should().Be(new Point(90, 6));
            points.Right.Should().Be(new Point(90, -6));
        }

        [Fact]
        public void CalculateArrowHead_WhenLengthIsTiny_ReturnsFinitePoints()
        {
            var service = new DeadlockGraphGeometryService();

            DeadlockArrowHeadPoints points = service.CalculateArrowHead(
                new Point(0, 0),
                new Point(0, 0));

            double.IsNaN(points.Left.X).Should().BeFalse();
            double.IsNaN(points.Right.Y).Should().BeFalse();
        }

        [Fact]
        public void CalculateConnectionPoints_WhenPositionsAreNull_Throws()
        {
            var service = new DeadlockGraphGeometryService();

            Action act = () => service.CalculateConnectionPoints(
                null!,
                "process1",
                "res_1");

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
