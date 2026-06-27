using System;
using System.Reflection;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum PlanSelectionSource
    {
        Missing,
        OperatorTree,
        VisualTree,
        GraphNode
    }

    public sealed record PlanSelectionResult(
        PlanSelectionSource Source,
        XElement? RelOp)
    {
        public bool HasSelection => RelOp != null;
    }

    public sealed class PlanSelectionActionService
    {
        public PlanSelectionResult SelectFromOperatorTreeItem(object? selectedValue)
        {
            XElement? relOp = selectedValue == null
                ? null
                : GetElementPropertyValue(selectedValue, "Tag");

            if (relOp != null)
            {
                return Selected(PlanSelectionSource.OperatorTree, relOp);
            }

            return Missing();
        }

        public PlanSelectionResult SelectFromVisualTreeNode(object? selectedValue)
        {
            if (selectedValue is PlanVisualNode node && node.Tag is XElement relOp)
            {
                return Selected(PlanSelectionSource.VisualTree, relOp);
            }

            return Missing();
        }

        public PlanSelectionResult SelectFromGraphNode(object? selectedValue)
        {
            XElement? relOp = selectedValue == null
                ? null
                : GetElementPropertyValue(selectedValue, "RawElement");

            return relOp == null
                ? Missing()
                : Selected(PlanSelectionSource.GraphNode, relOp);
        }

        private static XElement? GetElementPropertyValue(
            object value,
            string propertyName)
        {
            PropertyInfo? property = value
                .GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

            return property?.GetValue(value) as XElement;
        }

        private static PlanSelectionResult Selected(
            PlanSelectionSource source,
            XElement relOp)
        {
            ArgumentNullException.ThrowIfNull(relOp);

            return new PlanSelectionResult(source, relOp);
        }

        private static PlanSelectionResult Missing()
        {
            return new PlanSelectionResult(PlanSelectionSource.Missing, null);
        }
    }
}
