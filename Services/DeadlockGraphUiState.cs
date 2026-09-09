using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockGraphUiState
    {
        public Dictionary<string, Point> NodePositions { get; } = new();

        public Dictionary<string, FrameworkElement> NodeElements { get; } = new();

        public List<Core.Services.DeadlockGraphEdge> EdgesForDrawing { get; } = new();

        public Dictionary<Core.Services.DeadlockPlaybackEdgeKey, DeadlockGraphEdgeElements> ArrowCache { get; } = new();

        public Dictionary<string, (string LockType, string ObjectName)> ResourceGroupDetails { get; } = new();

        public Dictionary<Core.Services.DeadlockPlaybackEdgeKey, Border> StepBadges { get; } = new();
    }
}
