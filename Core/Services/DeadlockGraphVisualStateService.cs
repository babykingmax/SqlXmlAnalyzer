using System;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum DeadlockGraphDashPattern
    {
        None,
        Owner,
        Preview
    }

    public sealed record DeadlockGraphNodeVisualState(
        bool IsVisible,
        double Opacity,
        bool IsVictim,
        bool IsVictimRevealed,
        bool UseDefaultChrome);

    public sealed record DeadlockGraphEdgeVisualState(
        bool IsVisible,
        double Opacity,
        DeadlockGraphDashPattern DashPattern,
        int? BadgeStepNumber);

    public sealed class DeadlockGraphVisualStateService
    {
        public DeadlockGraphNodeVisualState CreatePlaybackNodeState(
            DeadlockPlaybackNodeState nodeState)
        {
            ArgumentNullException.ThrowIfNull(nodeState);

            if (nodeState.IsCollapsed)
            {
                return new DeadlockGraphNodeVisualState(
                    IsVisible: false,
                    Opacity: 0,
                    nodeState.IsVictim,
                    nodeState.IsVictimRevealed,
                    UseDefaultChrome: false);
            }

            return new DeadlockGraphNodeVisualState(
                IsVisible: true,
                Opacity: nodeState.IsActive ? 1.0 : 0.2,
                nodeState.IsVictim,
                nodeState.IsVictimRevealed,
                UseDefaultChrome: false);
        }

        public DeadlockGraphEdgeVisualState CreatePlaybackEdgeState(
            DeadlockPlaybackEdgeState edgeState)
        {
            ArgumentNullException.ThrowIfNull(edgeState);

            if (edgeState.IsCollapsed)
            {
                return new DeadlockGraphEdgeVisualState(
                    IsVisible: false,
                    Opacity: 0,
                    DeadlockGraphDashPattern.None,
                    BadgeStepNumber: null);
            }

            if (edgeState.IsActive)
            {
                return new DeadlockGraphEdgeVisualState(
                    IsVisible: true,
                    Opacity: 1.0,
                    DeadlockGraphDashPattern.None,
                    edgeState.BadgeStepNumber);
            }

            return new DeadlockGraphEdgeVisualState(
                IsVisible: true,
                Opacity: 0.2,
                DeadlockGraphDashPattern.Preview,
                BadgeStepNumber: null);
        }

        public DeadlockGraphNodeVisualState CreateResetNodeState()
        {
            return new DeadlockGraphNodeVisualState(
                IsVisible: true,
                Opacity: 1.0,
                IsVictim: false,
                IsVictimRevealed: false,
                UseDefaultChrome: true);
        }

        public DeadlockGraphEdgeVisualState CreateResetEdgeState(bool isWaitEdge)
        {
            return new DeadlockGraphEdgeVisualState(
                IsVisible: true,
                Opacity: 1.0,
                isWaitEdge ? DeadlockGraphDashPattern.None : DeadlockGraphDashPattern.Owner,
                BadgeStepNumber: null);
        }
    }
}
