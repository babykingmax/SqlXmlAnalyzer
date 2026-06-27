using System.Text;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphNodeClipboardInfo(
        string NodeId,
        string PhysicalOp,
        string LogicalOp,
        double SubtreeCost,
        int CostPercent,
        string EstimatedRows,
        string ActualRows,
        string EstimatedDataSize,
        string ObjectDetails,
        string OutputList,
        string SeekPredicates,
        string Predicate,
        string Warnings);

    public sealed class PlanGraphNodeClipboardService
    {
        public string BuildNodeInfo(PlanGraphNodeClipboardInfo node)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Node ID: {node.NodeId}");
            builder.AppendLine($"Physical Op: {node.PhysicalOp}");
            builder.AppendLine($"Logical Op: {node.LogicalOp}");
            builder.AppendLine($"Estimated Cost: {node.SubtreeCost} ({node.CostPercent:F1}%)");
            builder.AppendLine($"Estimated Rows: {node.EstimatedRows}");
            builder.AppendLine($"Actual Rows: {node.ActualRows}");
            builder.AppendLine($"Estimated Data Size: {node.EstimatedDataSize}");

            AppendOptional(builder, "Object", node.ObjectDetails);
            AppendOptional(builder, "Output List", node.OutputList);
            AppendOptional(builder, "Seek Predicates", node.SeekPredicates);
            AppendOptional(builder, "Predicate", node.Predicate);
            AppendOptional(builder, "Warnings", node.Warnings);

            return builder.ToString();
        }

        private static void AppendOptional(
            StringBuilder builder,
            string label,
            string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                builder.AppendLine($"{label}: {value}");
            }
        }
    }
}
