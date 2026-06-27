using System;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockGraphVisualStateServiceTests
    {
        [Fact]
        public void CreatePlaybackNodeState_WhenNodeIsActive_ReturnsVisibleFullOpacity()
        {
            var service = new DeadlockGraphVisualStateService();

            DeadlockGraphNodeVisualState result = service.CreatePlaybackNodeState(
                new DeadlockPlaybackNodeState(
                    "proc_id_1",
                    IsCollapsed: false,
                    IsActive: true,
                    IsVictim: false,
                    IsVictimRevealed: false));

            result.IsVisible.Should().BeTrue();
            result.Opacity.Should().Be(1.0);
            result.UseDefaultChrome.Should().BeFalse();
        }

        [Fact]
        public void CreatePlaybackNodeState_WhenNodeIsInactive_ReturnsDimmedVisibleState()
        {
            var service = new DeadlockGraphVisualStateService();

            DeadlockGraphNodeVisualState result = service.CreatePlaybackNodeState(
                new DeadlockPlaybackNodeState(
                    "proc_id_1",
                    IsCollapsed: false,
                    IsActive: false,
                    IsVictim: false,
                    IsVictimRevealed: false));

            result.IsVisible.Should().BeTrue();
            result.Opacity.Should().Be(0.2);
        }

        [Fact]
        public void CreatePlaybackNodeState_WhenNodeIsCollapsed_ReturnsHiddenState()
        {
            var service = new DeadlockGraphVisualStateService();

            DeadlockGraphNodeVisualState result = service.CreatePlaybackNodeState(
                new DeadlockPlaybackNodeState(
                    "proc_id_1",
                    IsCollapsed: true,
                    IsActive: true,
                    IsVictim: true,
                    IsVictimRevealed: true));

            result.IsVisible.Should().BeFalse();
            result.Opacity.Should().Be(0);
            result.IsVictim.Should().BeTrue();
            result.IsVictimRevealed.Should().BeTrue();
        }

        [Fact]
        public void CreatePlaybackEdgeState_WhenEdgeIsActive_ReturnsBadgeAndSolidLine()
        {
            var service = new DeadlockGraphVisualStateService();

            DeadlockGraphEdgeVisualState result = service.CreatePlaybackEdgeState(
                new DeadlockPlaybackEdgeState(
                    new DeadlockPlaybackEdgeKey("proc_id_1", "res_single_0"),
                    IsCollapsed: false,
                    IsActive: true,
                    BadgeStepNumber: 2));

            result.IsVisible.Should().BeTrue();
            result.Opacity.Should().Be(1.0);
            result.DashPattern.Should().Be(DeadlockGraphDashPattern.None);
            result.BadgeStepNumber.Should().Be(2);
        }

        [Fact]
        public void CreatePlaybackEdgeState_WhenEdgeIsInactive_ReturnsPreviewDashWithoutBadge()
        {
            var service = new DeadlockGraphVisualStateService();

            DeadlockGraphEdgeVisualState result = service.CreatePlaybackEdgeState(
                new DeadlockPlaybackEdgeState(
                    new DeadlockPlaybackEdgeKey("proc_id_1", "res_single_0"),
                    IsCollapsed: false,
                    IsActive: false,
                    BadgeStepNumber: 2));

            result.IsVisible.Should().BeTrue();
            result.Opacity.Should().Be(0.2);
            result.DashPattern.Should().Be(DeadlockGraphDashPattern.Preview);
            result.BadgeStepNumber.Should().BeNull();
        }

        [Fact]
        public void CreateResetStates_ReturnDefaultNodeAndDirectionalEdgeStyles()
        {
            var service = new DeadlockGraphVisualStateService();

            DeadlockGraphNodeVisualState nodeState = service.CreateResetNodeState();
            DeadlockGraphEdgeVisualState waitEdge = service.CreateResetEdgeState(isWaitEdge: true);
            DeadlockGraphEdgeVisualState ownerEdge = service.CreateResetEdgeState(isWaitEdge: false);

            nodeState.IsVisible.Should().BeTrue();
            nodeState.Opacity.Should().Be(1.0);
            nodeState.UseDefaultChrome.Should().BeTrue();
            waitEdge.DashPattern.Should().Be(DeadlockGraphDashPattern.None);
            ownerEdge.DashPattern.Should().Be(DeadlockGraphDashPattern.Owner);
        }

        [Fact]
        public void CreateStates_WhenArgumentsAreNull_Throw()
        {
            var service = new DeadlockGraphVisualStateService();

            Action nullNode = () => service.CreatePlaybackNodeState(null!);
            Action nullEdge = () => service.CreatePlaybackEdgeState(null!);

            nullNode.Should().Throw<ArgumentNullException>();
            nullEdge.Should().Throw<ArgumentNullException>();
        }
    }
}
