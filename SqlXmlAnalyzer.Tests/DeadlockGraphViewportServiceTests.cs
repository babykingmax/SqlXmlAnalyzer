using System;
using System.Collections.Generic;
using System.Windows;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockGraphViewportServiceTests
    {
        [Fact]
        public void CalculateZoomToFit_ReturnsScaleAndTranslationForProcessAndResourceNodes()
        {
            var service = new DeadlockGraphViewportService();
            var positions = new Dictionary<string, Point>
            {
                ["process1"] = new(80, 100),
                ["res_1"] = new(400, 200)
            };

            DeadlockViewportState? viewport = service.CalculateZoomToFit(
                positions,
                viewWidth: 800,
                viewHeight: 600);

            viewport.Should().NotBeNull();
            viewport!.Scale.Should().BeApproximately(1.333333, 0.0001);
            viewport.TranslateX.Should().BeApproximately(-26.6667, 0.0001);
            viewport.TranslateY.Should().BeApproximately(66.6667, 0.0001);
        }

        [Fact]
        public void CalculateZoomToFit_WhenViewSizeIsMissing_UsesDefaultViewport()
        {
            var service = new DeadlockGraphViewportService();
            var positions = new Dictionary<string, Point>
            {
                ["process1"] = new(80, 100),
                ["res_1"] = new(400, 200)
            };

            DeadlockViewportState? viewport = service.CalculateZoomToFit(
                positions,
                viewWidth: 0,
                viewHeight: -1);

            viewport.Should().NotBeNull();
            viewport!.Scale.Should().BeApproximately(1.333333, 0.0001);
        }

        [Fact]
        public void CalculateZoomToFit_ClampsScaleToMinimum()
        {
            var service = new DeadlockGraphViewportService();
            var positions = new Dictionary<string, Point>
            {
                ["process1"] = new(0, 0),
                ["process2"] = new(10000, 8000)
            };

            DeadlockViewportState? viewport = service.CalculateZoomToFit(
                positions,
                viewWidth: 500,
                viewHeight: 300);

            viewport.Should().NotBeNull();
            viewport!.Scale.Should().Be(0.2);
        }

        [Fact]
        public void CalculateZoomToFit_ClampsScaleToMaximum()
        {
            var service = new DeadlockGraphViewportService();
            var positions = new Dictionary<string, Point>
            {
                ["process1"] = new(0, 0)
            };

            DeadlockViewportState? viewport = service.CalculateZoomToFit(
                positions,
                viewWidth: 2000,
                viewHeight: 1600);

            viewport.Should().NotBeNull();
            viewport!.Scale.Should().Be(2.0);
        }

        [Fact]
        public void CalculateZoomToFit_WhenNoNodes_ReturnsNull()
        {
            var service = new DeadlockGraphViewportService();

            DeadlockViewportState? viewport = service.CalculateZoomToFit(
                new Dictionary<string, Point>(),
                viewWidth: 800,
                viewHeight: 600);

            viewport.Should().BeNull();
        }

        [Fact]
        public void CalculateZoomToFit_WhenPositionsAreNull_Throws()
        {
            var service = new DeadlockGraphViewportService();

            Action act = () => service.CalculateZoomToFit(
                null!,
                viewWidth: 800,
                viewHeight: 600);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
