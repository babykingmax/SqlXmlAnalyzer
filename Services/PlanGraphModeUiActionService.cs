using System;
using System.Collections.Generic;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphModeUiActionService
    {
        public void ApplyViewMode(
            int selectedIndex,
            IEnumerable<PlanNodeViewModel> nodes)
        {
            if (selectedIndex < 0)
            {
                return;
            }

            ApplyViewMode((DiagramViewMode)selectedIndex, nodes);
        }

        public void ApplyLayoutMode(
            int selectedIndex,
            Action<PlanLayoutMode> setLayoutMode)
        {
            if (selectedIndex < 0)
            {
                return;
            }

            setLayoutMode((PlanLayoutMode)selectedIndex);
        }

        public void ApplyColorMode(
            int selectedIndex,
            Action<PlanColorMode> setColorMode)
        {
            if (selectedIndex < 0)
            {
                return;
            }

            setColorMode((PlanColorMode)selectedIndex);
        }

        public void ApplyLinkMetric(
            int selectedIndex,
            Action<LinkMetricMode> setLinkMetric)
        {
            if (selectedIndex < 0)
            {
                return;
            }

            setLinkMetric((LinkMetricMode)selectedIndex);
        }

        public void ApplyViewMode(
            DiagramViewMode viewMode,
            IEnumerable<PlanNodeViewModel> nodes)
        {
            ArgumentNullException.ThrowIfNull(nodes);

            foreach (PlanNodeViewModel node in nodes)
            {
                node.ViewMode = viewMode;
            }
        }

        public void ApplyColorMode(
            PlanColorMode colorMode,
            IEnumerable<PlanNodeViewModel> nodes)
        {
            ArgumentNullException.ThrowIfNull(nodes);

            foreach (PlanNodeViewModel node in nodes)
            {
                node.ColorMode = colorMode;
            }
        }

        public void ApplyLinkMetric(
            LinkMetricMode linkMetric,
            IEnumerable<ConnectionViewModel> connections)
        {
            ArgumentNullException.ThrowIfNull(connections);

            foreach (ConnectionViewModel connection in connections)
            {
                connection.CurrentLinkMetric = linkMetric;
            }
        }
    }
}
