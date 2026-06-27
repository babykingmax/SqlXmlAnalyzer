using System.Text;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum PlanGraphConnectionMetricKind
    {
        RowCount,
        DataSize
    }

    public enum PlanGraphConnectionStrokeKey
    {
        Default,
        Red,
        Orange,
        Green
    }

    public sealed record PlanGraphConnectionNodeInfo(
        string PhysicalOp,
        double EstimatedRows,
        string? ActualRowsText,
        double ActualRows,
        double AverageRowSize);

    public sealed class PlanGraphConnectionDisplayService
    {
        public double CalculateRowsCount(PlanGraphConnectionNodeInfo? source)
        {
            if (source == null)
            {
                return 0;
            }

            return source.ActualRows > 0
                ? source.ActualRows
                : source.EstimatedRows;
        }

        public double CalculateDataSize(PlanGraphConnectionNodeInfo? source)
        {
            if (source == null)
            {
                return 0;
            }

            double rows = CalculateRowsCount(source);
            return rows * source.AverageRowSize;
        }

        public double GetMetricValue(
            PlanGraphConnectionMetricKind metricKind,
            PlanGraphConnectionNodeInfo? source)
        {
            return metricKind switch
            {
                PlanGraphConnectionMetricKind.DataSize => CalculateDataSize(source),
                _ => CalculateRowsCount(source)
            };
        }

        public string BuildLabel(
            PlanGraphConnectionMetricKind metricKind,
            PlanGraphConnectionNodeInfo? source)
        {
            double metricValue = GetMetricValue(metricKind, source);
            return metricKind switch
            {
                PlanGraphConnectionMetricKind.DataSize =>
                    PlanGraphMetricService.FormatBytes(metricValue),
                _ => PlanGraphMetricService.FormatNumber(metricValue)
            };
        }

        public PlanGraphConnectionStrokeKey GetStrokeKey(
            PlanGraphConnectionNodeInfo? source)
        {
            if (source == null || !HasActualRowsForStroke(source))
            {
                return PlanGraphConnectionStrokeKey.Default;
            }

            double estimatedRows = source.EstimatedRows <= 0
                ? 1.0
                : source.EstimatedRows;
            double actualRows = source.ActualRows <= 0
                ? 1.0
                : source.ActualRows;
            double ratio = actualRows / estimatedRows;

            if (ratio > 5.0 || ratio < 0.2)
            {
                return PlanGraphConnectionStrokeKey.Red;
            }

            if (ratio > 2.0 || ratio < 0.5)
            {
                return PlanGraphConnectionStrokeKey.Orange;
            }

            return PlanGraphConnectionStrokeKey.Green;
        }

        public string BuildToolTip(
            PlanGraphConnectionNodeInfo? source,
            string? targetPhysicalOp)
        {
            if (source == null)
            {
                return "未知数据流";
            }

            bool hasActualRows = HasActualRowsForTooltip(source);
            string estimatedRowsText =
                PlanGraphMetricService.FormatNumber(source.EstimatedRows);
            string actualRowsText = hasActualRows
                ? PlanGraphMetricService.FormatNumber(source.ActualRows)
                : "N/A";
            string estimatedSizeText =
                PlanGraphMetricService.FormatBytes(
                    source.EstimatedRows * source.AverageRowSize);
            string actualSizeText = hasActualRows
                ? PlanGraphMetricService.FormatBytes(
                    source.ActualRows * source.AverageRowSize)
                : "N/A";

            var builder = new StringBuilder();
            builder.AppendLine($"数据流: {source.PhysicalOp} ➔ {targetPhysicalOp}");
            builder.AppendLine($"预估行数: {estimatedRowsText} ({source.EstimatedRows:N0})");

            if (actualRowsText != "N/A")
            {
                builder.AppendLine($"实际行数: {actualRowsText} ({source.ActualRows:N0})");
            }

            builder.AppendLine($"平均行宽: {source.AverageRowSize:N0} 字节");
            builder.AppendLine($"预估大小: {estimatedSizeText}");

            if (actualSizeText != "N/A")
            {
                builder.AppendLine($"实际大小: {actualSizeText}");
                double ratio = source.EstimatedRows > 0
                    ? source.ActualRows / source.EstimatedRows
                    : 1.0;
                builder.AppendLine($"估算偏差: {ratio:F2} 倍");

                if (ratio > 5.0)
                {
                    builder.AppendLine("⚠️ 严重低估 (可能会引发非最优物理算法选择！)");
                }
                else if (ratio < 0.2)
                {
                    builder.AppendLine("⚠️ 严重高估 (可能会导致过度的内存申请排队！)");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static bool HasActualRowsForStroke(
            PlanGraphConnectionNodeInfo source)
        {
            return source.ActualRows > 0
                || (!string.IsNullOrEmpty(source.ActualRowsText)
                    && source.ActualRowsText != "N/A");
        }

        private static bool HasActualRowsForTooltip(
            PlanGraphConnectionNodeInfo source)
        {
            return !string.IsNullOrEmpty(source.ActualRowsText)
                && source.ActualRowsText != "N/A";
        }
    }
}
