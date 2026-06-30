using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphCollapseUiActionService
    {
        private readonly Core.Services.PlanGraphCollapseStateService _collapseStateService = new();
        private readonly Core.Services.PlanGraphVisibilityStateService _visibilityStateService = new();

        public IReadOnlyDictionary<XElement, bool> CalculateExpandAll(
            IReadOnlyList<PlanNodeViewModel> masterNodes)
        {
            return _collapseStateService.CalculateExpandAll(
                BuildCollapseStateNodes(masterNodes)).CollapsedStates;
        }

        public IReadOnlyDictionary<XElement, bool> CalculateSmartCollapse(
            IReadOnlyList<PlanNodeViewModel> masterNodes)
        {
            return _collapseStateService.CalculateSmartCollapse(
                BuildCollapseStateNodes(masterNodes)).CollapsedStates;
        }

        public IReadOnlyDictionary<XElement, bool> CalculateToggle(
            IReadOnlyList<PlanNodeViewModel> masterNodes,
            XElement targetRelOp)
        {
            return _collapseStateService.CalculateToggle(
                BuildCollapseStateNodes(masterNodes),
                targetRelOp).CollapsedStates;
        }

        public void ApplyCollapseStates(
            IEnumerable<PlanNodeViewModel> masterNodes,
            IReadOnlyDictionary<XElement, bool> collapsedStates)
        {
            ArgumentNullException.ThrowIfNull(masterNodes);
            ArgumentNullException.ThrowIfNull(collapsedStates);

            foreach (PlanNodeViewModel node in masterNodes)
            {
                node.IsCollapsed =
                    node.RawElement != null
                    && collapsedStates.TryGetValue(node.RawElement, out bool isCollapsed)
                    && isCollapsed;
            }
        }

        public void ToggleNode(
            PlanNodeViewModel node,
            IReadOnlyList<PlanNodeViewModel> masterNodes,
            IReadOnlyList<ConnectionViewModel> masterConnections,
            Action reapplyLayout,
            Action updateVisibility)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(masterNodes);
            ArgumentNullException.ThrowIfNull(masterConnections);
            ArgumentNullException.ThrowIfNull(reapplyLayout);
            ArgumentNullException.ThrowIfNull(updateVisibility);

            try
            {
                var logService = new Core.Services.PlanGraphCollapseLogService();
                DateTime timestamp = DateTime.Now;
                Core.Services.PlanGraphCollapseLogNode nodeBeforeToggle =
                    ToCollapseLogNode(node);
                AppendCollapseLog(logService.BuildStartLine(nodeBeforeToggle, timestamp));
                Core.Services.PlanGraphCollapseLogSnapshot oldSnapshot =
                    CaptureLogSnapshot(masterNodes, masterConnections);

                if (node.RawElement != null)
                {
                    ApplyCollapseStates(
                        masterNodes,
                        CalculateToggle(masterNodes, node.RawElement));
                }
                else
                {
                    node.IsCollapsed = !node.IsCollapsed;
                }

                reapplyLayout();
                updateVisibility();

                Core.Services.PlanGraphCollapseLogSnapshot newSnapshot =
                    CaptureLogSnapshot(masterNodes, masterConnections);
                AppendCollapseLog(
                    logService.BuildToggleLog(
                        nodeBeforeToggle,
                        node.IsCollapsed,
                        oldSnapshot,
                        newSnapshot,
                        timestamp));
            }
            catch (Exception ex)
            {
                try
                {
                    var logService = new Core.Services.PlanGraphCollapseLogService();
                    AppendCollapseLog(logService.BuildExceptionLog(ex, DateTime.Now));
                }
                catch
                {
                }
            }
        }

        public void UpdateVisibility(
            XDocument? currentDocument,
            XNamespace? currentNamespace,
            IReadOnlyList<PlanNodeViewModel> masterNodes,
            IReadOnlyList<ConnectionViewModel> masterConnections,
            ICollection<PlanNodeViewModel> visibleNodes,
            ICollection<ConnectionViewModel> visibleConnections)
        {
            if (currentDocument == null
                || currentNamespace == null
                || masterNodes.Count == 0)
            {
                return;
            }

            XNamespace ns = currentNamespace;
            List<XElement> relOps =
                currentDocument.Descendants(ns + "RelOp").ToList();

            IReadOnlyList<Core.Services.PlanGraphVisibilityStateNode> visibilityNodes =
                masterNodes
                    .Where(node => node.RawElement != null)
                    .Select(node => new Core.Services.PlanGraphVisibilityStateNode(
                        node.RawElement!,
                        node.IsCollapsed))
                    .ToList();
            IReadOnlyList<Core.Services.PlanGraphVisibilityStateConnection> visibilityConnections =
                masterConnections
                    .Where(connection =>
                        connection.Source?.RawElement != null
                        && connection.Target?.RawElement != null)
                    .Select(connection => new Core.Services.PlanGraphVisibilityStateConnection(
                        connection.Source!.RawElement!,
                        connection.Target!.RawElement!))
                    .ToList();

            Core.Services.PlanGraphVisibilityStateResult visibility =
                _visibilityStateService.Calculate(
                    relOps,
                    ns,
                    visibilityNodes,
                    visibilityConnections);

            foreach (PlanNodeViewModel node in masterNodes)
            {
                node.IsVisible = node.RawElement != null
                    && visibility.VisibleRelOps.Contains(node.RawElement);

                if (!visibleNodes.Contains(node))
                {
                    visibleNodes.Add(node);
                }
            }

            foreach (ConnectionViewModel connection in masterConnections)
            {
                connection.IsVisible =
                    connection.Source?.RawElement != null
                    && connection.Target?.RawElement != null
                    && visibility.VisibleConnections.Contains(
                        new Core.Services.PlanGraphVisibilityStateConnection(
                            connection.Source.RawElement,
                            connection.Target.RawElement));

                if (!visibleConnections.Contains(connection))
                {
                    visibleConnections.Add(connection);
                }
            }
        }

        public Core.Services.PlanGraphCollapseLogSnapshot CaptureLogSnapshot(
            IEnumerable<PlanNodeViewModel> masterNodes,
            IEnumerable<ConnectionViewModel> masterConnections)
        {
            ArgumentNullException.ThrowIfNull(masterNodes);
            ArgumentNullException.ThrowIfNull(masterConnections);

            return new Core.Services.PlanGraphCollapseLogSnapshot(
                masterNodes
                    .Where(node => node.IsVisible)
                    .Select(ToCollapseLogNode)
                    .ToList(),
                masterConnections
                    .Where(connection => connection.IsVisible)
                    .Select(ToCollapseLogConnection)
                    .ToList());
        }

        public Core.Services.PlanGraphCollapseLogNode ToCollapseLogNode(
            PlanNodeViewModel node)
        {
            ArgumentNullException.ThrowIfNull(node);

            return new Core.Services.PlanGraphCollapseLogNode(
                node.NodeId,
                node.PhysicalOp,
                node.IsCollapsed);
        }

        public void AppendCollapseLog(string text)
        {
            string logDir = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Logs");
            if (!System.IO.Directory.Exists(logDir))
            {
                System.IO.Directory.CreateDirectory(logDir);
            }

            string logFile = System.IO.Path.Combine(logDir, "CollapseLog.txt");
            System.IO.File.AppendAllText(logFile, text);
        }

        private static IReadOnlyList<Core.Services.PlanGraphCollapseStateNode> BuildCollapseStateNodes(
            IEnumerable<PlanNodeViewModel> masterNodes)
        {
            ArgumentNullException.ThrowIfNull(masterNodes);

            return masterNodes
                .Where(node => node.RawElement != null)
                .Select(node => new Core.Services.PlanGraphCollapseStateNode(
                    node.RawElement!,
                    node.HasChildren,
                    node.SubtreeCost,
                    node.NodeSeverity,
                    node.IsCollapsed))
                .ToList();
        }

        private static Core.Services.PlanGraphCollapseLogConnection ToCollapseLogConnection(
            ConnectionViewModel connection)
        {
            return new Core.Services.PlanGraphCollapseLogConnection(
                connection.Source?.NodeId,
                connection.Source?.PhysicalOp,
                connection.Target?.NodeId,
                connection.Target?.PhysicalOp);
        }
    }
}
