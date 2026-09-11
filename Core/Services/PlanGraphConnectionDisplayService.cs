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
        bool HasActualRows,
        double ActualRows,
        double AverageRowSize)
    {
        public Models.PlanOperatorFacts? Facts { get; init; }
    }

    public sealed class PlanGraphConnectionDisplayService
    {
        public double CalculateRowsCount(PlanGraphConnectionNodeInfo? source)
        {
            if (source == null)
            {
                return 0;
            }

            return source.HasActualRows
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
            if (source?.Facts is { } facts && ((!facts.OutputRows.IsAvailable && !facts.EstimatedRows.IsAvailable)
                || metricKind == PlanGraphConnectionMetricKind.DataSize && !facts.AverageRowSize.IsAvailable)) return "N/A";
            double metricValue = GetMetricValue(metricKind, source);
            if (!double.IsFinite(metricValue)) return "N/A";
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
            if (source == null || !source.HasActualRows || source.Facts is { } facts
                && (!facts.EstimatedRows.IsAvailable || !facts.RowsPerExecution.IsAvailable))
            {
                return PlanGraphConnectionStrokeKey.Default;
            }

            double estimatedRows = source.EstimatedRows <= 0
                ? 1.0
                : source.EstimatedRows;
            double comparableRows = source.Facts == null ? source.ActualRows : (double)(source.Facts.RowsPerExecution.Value ?? 0);
            double actualRows = comparableRows <= 0
                ? 1.0
                : comparableRows;
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
            if (source.Facts is { } facts) return BuildFactsToolTip(source.PhysicalOp, targetPhysicalOp, facts);

            bool hasActualRows = source.HasActualRows;
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

            if (hasActualRows)
            {
                builder.AppendLine($"实际行数: {actualRowsText} ({source.ActualRows:N0})");
            }

            builder.AppendLine($"平均行宽: {source.AverageRowSize:N0} 字节");
            builder.AppendLine($"预估大小: {estimatedSizeText}");

            if (hasActualRows)
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

        private static string BuildFactsToolTip(string physicalOp, string? target, Models.PlanOperatorFacts facts)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"数据流: {physicalOp} ➔ {target}");
            builder.AppendLine($"预估行数（每次执行）: {facts.EstimatedRows.Display("N0")}");
            if (facts.OutputRows.IsAvailable) builder.AppendLine($"实际行数: {facts.OutputRows.Display("N0")}（全部线程/执行的总输出）");
            builder.AppendLine($"实际读取行: {facts.RowsRead.Display("N0")}");
            builder.AppendLine($"平均行宽: {facts.AverageRowSize.Display("N0")} 字节");
            string Size(double? rows) => rows.HasValue && facts.AverageRowSize.IsAvailable
                && double.IsFinite(rows.Value * facts.AverageRowSize.Value!.Value)
                ? PlanGraphMetricService.FormatBytes(rows.Value * facts.AverageRowSize.Value.Value) : "N/A";
            builder.AppendLine($"预估大小（每次执行）: {Size(facts.EstimatedRows.Value)}");
            if (facts.OutputRows.IsAvailable) builder.AppendLine($"实际大小（按估算行宽推算）: {Size((double?)facts.OutputRows.Value)}");
            builder.AppendLine($"线程执行次数合计: {facts.ThreadExecutions.Display("N0")}");
            builder.AppendLine($"逻辑执行次数: {facts.LogicalExecutions.Display("N0")}");
            if (facts.EstimatedRows.IsAvailable && facts.RowsPerExecution.IsAvailable && facts.EstimatedRows.Value > 0)
            {
                double ratio = (double)facts.RowsPerExecution.Value!.Value / facts.EstimatedRows.Value!.Value;
                builder.AppendLine($"估算偏差: {ratio:F2} 倍（每次执行输出行）");
            }
            return builder.ToString().TrimEnd();
        }
    }
}
