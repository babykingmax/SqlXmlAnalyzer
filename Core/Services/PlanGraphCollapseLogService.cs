using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphCollapseLogNode(
        string NodeId,
        string PhysicalOp,
        bool IsCollapsed);

    public sealed record PlanGraphCollapseLogConnection(
        string? SourceNodeId,
        string? SourcePhysicalOp,
        string? TargetNodeId,
        string? TargetPhysicalOp);

    public sealed record PlanGraphCollapseLogSnapshot(
        IReadOnlyList<PlanGraphCollapseLogNode> VisibleNodes,
        IReadOnlyList<PlanGraphCollapseLogConnection> VisibleConnections);

    public sealed class PlanGraphCollapseLogService
    {
        public string BuildStartLine(
            PlanGraphCollapseLogNode node,
            DateTime timestamp)
        {
            return $"\n[{FormatTime(timestamp)}] --- START CLICK: {GetActionLabel(node.IsCollapsed)} on [{node.NodeId}] {node.PhysicalOp} ---\n";
        }

        public string BuildToggleLog(
            PlanGraphCollapseLogNode nodeBeforeToggle,
            bool newCollapsedState,
            PlanGraphCollapseLogSnapshot oldSnapshot,
            PlanGraphCollapseLogSnapshot newSnapshot,
            DateTime timestamp)
        {
            string time = FormatTime(timestamp);
            IReadOnlyList<PlanGraphCollapseLogNode> addedNodes =
                ExceptByKey(newSnapshot.VisibleNodes, oldSnapshot.VisibleNodes, NodeKey);
            IReadOnlyList<PlanGraphCollapseLogNode> removedNodes =
                ExceptByKey(oldSnapshot.VisibleNodes, newSnapshot.VisibleNodes, NodeKey);
            IReadOnlyList<PlanGraphCollapseLogConnection> addedConnections =
                ExceptByKey(newSnapshot.VisibleConnections, oldSnapshot.VisibleConnections, ConnectionKey);
            IReadOnlyList<PlanGraphCollapseLogConnection> removedConnections =
                ExceptByKey(oldSnapshot.VisibleConnections, newSnapshot.VisibleConnections, ConnectionKey);

            var builder = new StringBuilder();
            builder.AppendLine("==================================================");
            builder.AppendLine($"[{time}] Action: {GetActionLabel(nodeBeforeToggle.IsCollapsed)} on Node [{nodeBeforeToggle.NodeId}] {nodeBeforeToggle.PhysicalOp}");
            builder.AppendLine($"[{time}] Toggled IsCollapsed to {newCollapsedState}");
            builder.AppendLine($"[{time}] ReapplyLayout Completed");
            builder.AppendLine($"[{time}] UpdateGraphVisibility Completed");

            builder.AppendLine($"[{time}] Nodes Added (Expanded): {addedNodes.Count}");
            foreach (PlanGraphCollapseLogNode node in addedNodes)
            {
                builder.AppendLine($"  + [{node.NodeId}] {node.PhysicalOp} (Collapsed State: {node.IsCollapsed})");
            }

            builder.AppendLine($"[{time}] Nodes Removed (Hidden): {removedNodes.Count}");
            foreach (PlanGraphCollapseLogNode node in removedNodes)
            {
                builder.AppendLine($"  - [{node.NodeId}] {node.PhysicalOp}");
            }

            builder.AppendLine($"[{time}] Connections Added: {addedConnections.Count}");
            foreach (PlanGraphCollapseLogConnection connection in addedConnections)
            {
                builder.AppendLine($"  + [{connection.SourceNodeId}] {connection.SourcePhysicalOp} --> [{connection.TargetNodeId}] {connection.TargetPhysicalOp}");
            }

            builder.AppendLine($"[{time}] Connections Removed: {removedConnections.Count}");
            foreach (PlanGraphCollapseLogConnection connection in removedConnections)
            {
                builder.AppendLine($"  - [{connection.SourceNodeId}] {connection.SourcePhysicalOp} --> [{connection.TargetNodeId}] {connection.TargetPhysicalOp}");
            }

            builder.AppendLine("==================================================");
            return builder.ToString();
        }

        public string BuildExceptionLog(
            Exception exception,
            DateTime timestamp)
        {
            return $"\n[{FormatTime(timestamp)}] [EXCEPTION CAUGHT]: {exception}\n";
        }

        private static string GetActionLabel(bool isCollapsed)
        {
            return isCollapsed ? "Expand [+]" : "Collapse [-]";
        }

        private static string FormatTime(DateTime timestamp)
        {
            return timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string NodeKey(PlanGraphCollapseLogNode node)
        {
            return node.NodeId;
        }

        private static string ConnectionKey(PlanGraphCollapseLogConnection connection)
        {
            return $"{connection.SourceNodeId}->{connection.TargetNodeId}";
        }

        private static IReadOnlyList<T> ExceptByKey<T>(
            IEnumerable<T> source,
            IEnumerable<T> other,
            Func<T, string> keySelector)
        {
            HashSet<string> otherKeys = other.Select(keySelector).ToHashSet();
            return source
                .Where(item => !otherKeys.Contains(keySelector(item)))
                .ToList();
        }
    }
}
