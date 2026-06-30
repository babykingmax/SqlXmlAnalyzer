using System;
using System.Collections.Generic;
using System.Windows;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphViewportUiActionService
    {
        public void ResetView(
            Action<double> setViewportZoom,
            IReadOnlyList<PlanNodeViewModel> nodes)
        {
            ArgumentNullException.ThrowIfNull(setViewportZoom);
            ArgumentNullException.ThrowIfNull(nodes);

            setViewportZoom(1.0);

            if (nodes.Count == 0)
            {
                return;
            }

            Point first = nodes[0].Location;
            nodes[0].Location = new Point(first.X + 1, first.Y);
            nodes[0].Location = first;
        }
    }
}
