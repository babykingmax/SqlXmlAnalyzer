using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphNodeWarningSettings(
        double ResidualIOThreshold,
        int ResidualIOMinRowsRead);

    public sealed class PlanGraphNodeBuildResult
    {
        public required XElement RawElement { get; init; }
        public required string NodeId { get; init; }
        public required string PhysicalOp { get; init; }
        public required string LogicalOp { get; init; }
        public required string ExecutionMode { get; init; }
        public double Cost { get; init; }
        public double OwnCost { get; init; }
        public double ActualRecost { get; init; }
        public double SubtreeCost { get; init; }
        public int CostPercent { get; init; }
        public required string EstRows { get; init; }
        public double EstRowsNum { get; init; }
        public required string EstimatedRowsToBeRead { get; init; }
        public double EstimatedCPUCostNum { get; init; }
        public double EstimatedIOCostNum { get; init; }
        public double AvgRowSizeNum { get; init; }
        public required string EstimatedIOCost { get; init; }
        public required string EstimatedCPUCost { get; init; }
        public required string EstimatedExecutions { get; init; }
        public required string ActualExecutions { get; init; }
        public required string ActualRows { get; init; }
        public required string ActualRowsRead { get; init; }
        public double ActualRowsNum { get; init; }
        public required string EstimatedOperatorCost { get; init; }
        public required string EstimatedSubtreeCostStr { get; init; }
        public required string EstimatedRowSize { get; init; }
        public required string EstimatedDataSize { get; init; }
        public required string ActualDataSize { get; init; }
        public required string ActualRebinds { get; init; }
        public required string ActualRewinds { get; init; }
        public required string Ordered { get; init; }
        public required string DatabaseName { get; init; }
        public required string TableName { get; init; }
        public required string IndexName { get; init; }
        public required string SeekPredicates { get; init; }
        public required string Predicate { get; init; }
        public required string OutputList { get; init; }
        public required string ObjectDetails { get; init; }
        public required string Partitioned { get; init; }
        public required string PartitionCount { get; init; }
        public required string PartitionRange { get; init; }
        public bool IsParallel { get; init; }
        public required string Warnings { get; init; }
        public required string NodeSeverity { get; init; }
        public required string OperatorType { get; init; }
    }

    public sealed class PlanGraphNodeBuilderService
    {
        private readonly RuleEngine _ruleEngine = new();
        private readonly PlanGraphOperatorTypeService _operatorTypeService = new();
        private readonly PlanGraphRelOpDetailsService _relOpDetailsService = new();
        private readonly PlanGraphRuntimeCountersService _runtimeCountersService = new();
        private readonly PlanGraphWarningService _warningService = new();

        public PlanGraphNodeBuilderService()
        {
            _ruleEngine.RegisterDefaultRules();
        }

        public PlanGraphNodeBuildResult Build(
            XElement relOp,
            XNamespace ns,
            PlanGraphNodeWarningSettings warningSettings)
        {
            ArgumentNullException.ThrowIfNull(relOp);

            string nodeId = relOp.Attribute("NodeId")?.Value ?? "?";
            string physical = relOp.Attribute("PhysicalOp")?.Value ?? relOp.Attribute("LogicalOp")?.Value ?? "Unknown";
            string logical = relOp.Attribute("LogicalOp")?.Value ?? "Unknown";

            double estimatedRows = ParseDouble(relOp.Attribute("EstimateRows")?.Value);
            double estimatedRowsRead = ParseDouble(relOp.Attribute("EstimatedRowsRead")?.Value, estimatedRows);
            double subtreeCost = ParseDouble(relOp.Attribute("EstimatedTotalSubtreeCost")?.Value);

            string estimatedIoCost = relOp.Attribute("EstimateIO")?.Value ?? "0";
            string estimatedCpuCost = relOp.Attribute("EstimateCPU")?.Value ?? "0";
            string estimatedExecutions = relOp.Attribute("EstimateRebinds") != null
                ? (ParseDouble(relOp.Attribute("EstimateRebinds")?.Value)
                    + ParseDouble(relOp.Attribute("EstimateRewinds")?.Value)
                    + 1.0).ToString("0.0")
                : "1.0";
            string estimatedRowSize = relOp.Attribute("AvgRowSize")?.Value ?? "0";

            PlanGraphRuntimeCountersResult runtimeCounters =
                _runtimeCountersService.Parse(relOp, ns);
            PlanGraphRelOpDetails relOpDetails =
                _relOpDetailsService.Parse(relOp, ns, physical);
            string residualPredicate = string.Join(" AND ", relOpDetails.Predicates);
            string seekPredicate = string.Join(" AND ", relOpDetails.SeekPredicates);

            double estimatedRowSizeNumber = ParseDouble(estimatedRowSize);
            double estimatedCpuNumber = ParseDouble(estimatedCpuCost);
            double estimatedIoNumber = ParseDouble(estimatedIoCost);
            double estimatedDataSizeMB = (estimatedRows * estimatedRowSizeNumber) / (1024.0 * 1024.0);
            double actualDataSizeMB = runtimeCounters.HasActual
                ? (runtimeCounters.ActualRows * estimatedRowSizeNumber) / (1024.0 * 1024.0)
                : 0.0;
            string estimatedDataSize = FormatDataSizeMB(estimatedDataSizeMB);
            string actualDataSize = FormatDataSizeMB(actualDataSizeMB);

            PlanGraphWarningResult warningResult =
                _warningService.BuildWarnings(
                    relOp,
                    ns,
                    new PlanGraphWarningContext(
                        nodeId,
                        physical,
                        residualPredicate,
                        seekPredicate,
                        runtimeCounters.HasActual,
                        runtimeCounters.HasActualRead,
                        runtimeCounters.ActualRows,
                        runtimeCounters.ActualRowsRead,
                        runtimeCounters.IsThreadDataSkewed,
                        warningSettings.ResidualIOThreshold,
                        warningSettings.ResidualIOMinRowsRead),
                    _ruleEngine.AnalyzeNode(relOp, ns));

            bool isParallel =
                relOp.Attribute("Parallel")?.Value == "1"
                || relOp.Descendants(ns + "ThreadStat").Any()
                || physical.Contains("Parallelism");

            double ownCost = subtreeCost;
            double actualRecost = CalculateActualRecost(
                ownCost,
                estimatedRows,
                runtimeCounters.ActualRows,
                runtimeCounters.HasActual);

            return new PlanGraphNodeBuildResult
            {
                RawElement = relOp,
                NodeId = nodeId,
                PhysicalOp = physical,
                LogicalOp = logical,
                ExecutionMode = "Row",
                Cost = ownCost,
                OwnCost = ownCost,
                ActualRecost = actualRecost,
                SubtreeCost = subtreeCost,
                CostPercent = 1,
                EstRows = PlanGraphMetricService.FormatNumber(estimatedRows),
                EstRowsNum = estimatedRows,
                EstimatedRowsToBeRead = PlanGraphMetricService.FormatNumber(estimatedRowsRead),
                EstimatedCPUCostNum = estimatedCpuNumber,
                EstimatedIOCostNum = estimatedIoNumber,
                AvgRowSizeNum = estimatedRowSizeNumber,
                EstimatedIOCost = estimatedIoCost,
                EstimatedCPUCost = estimatedCpuCost,
                EstimatedExecutions = estimatedExecutions,
                ActualExecutions = runtimeCounters.HasActual ? runtimeCounters.ActualExecutions.ToString("F0") : string.Empty,
                ActualRows = runtimeCounters.HasActual
                    ? runtimeCounters.ActualRows.ToString("N0", CultureInfo.InvariantCulture)
                    : string.Empty,
                ActualRowsRead = runtimeCounters.HasActual && runtimeCounters.HasActualRead
                    ? runtimeCounters.ActualRowsRead.ToString("N0")
                    : string.Empty,
                ActualRowsNum = runtimeCounters.ActualRows,
                EstimatedOperatorCost = ownCost.ToString("0.0000000"),
                EstimatedSubtreeCostStr = subtreeCost.ToString("0.0000000"),
                EstimatedRowSize = estimatedRowSizeNumber.ToString("0") + " B",
                EstimatedDataSize = estimatedDataSize,
                ActualDataSize = runtimeCounters.HasActual ? actualDataSize : string.Empty,
                ActualRebinds = runtimeCounters.HasActual ? runtimeCounters.ActualRebinds.ToString() : string.Empty,
                ActualRewinds = runtimeCounters.HasActual ? runtimeCounters.ActualRewinds.ToString() : string.Empty,
                Ordered = relOp.Attribute("LogicalOp")?.Value?.Contains("Sort") == true ? "True" : "False",
                DatabaseName = relOpDetails.DatabaseName,
                TableName = relOpDetails.TableName,
                IndexName = relOpDetails.IndexName,
                SeekPredicates = string.Join("\n", relOpDetails.SeekPredicates),
                Predicate = string.Join("\n", relOpDetails.Predicates),
                OutputList = string.Join(", ", relOpDetails.OutputColumns),
                ObjectDetails = relOpDetails.ObjectDetails,
                Partitioned = relOpDetails.IsPartitioned ? "True" : "False",
                PartitionCount = relOpDetails.PartitionCount,
                PartitionRange = relOpDetails.PartitionRange,
                IsParallel = isParallel,
                Warnings = warningResult.WarningsText,
                NodeSeverity = warningResult.HighestSeverity,
                OperatorType = _operatorTypeService.DetectOperatorType(physical, logical)
            };
        }

        private static double CalculateActualRecost(
            double ownCost,
            double estimatedRows,
            double actualRows,
            bool hasActual)
        {
            if (!hasActual || estimatedRows <= 0)
            {
                return ownCost;
            }

            double actualRecost = ownCost * (actualRows / estimatedRows);
            return double.IsInfinity(actualRecost) || double.IsNaN(actualRecost)
                ? ownCost
                : actualRecost;
        }

        private static string FormatDataSizeMB(double sizeMB)
        {
            return sizeMB < 1.0
                ? $"{sizeMB * 1024:F0} KB"
                : $"{sizeMB:F0} MB";
        }

        private static double ParseDouble(
            string? value,
            double defaultValue = 0.0)
        {
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            return double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : defaultValue;
        }
    }
}
