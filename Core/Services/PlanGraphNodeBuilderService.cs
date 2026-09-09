using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphNodeWarningSettings(
        double ResidualIOThreshold,
        int ResidualIOMinRowsRead);

    public sealed class PlanGraphNodeBuildResult
    {
        public required XElement RawElement { get; init; }
        public Models.PlanOperatorFacts? Facts { get; init; }
        public PlanDiagnosticReport? Diagnostics { get; init; }
        public required string NodeId { get; init; }
        public Models.PlanOperatorKey? Identity { get; init; }
        public Models.PlanLocation? SourceLocation { get; init; }
        public System.Collections.Generic.IReadOnlyList<Models.SqlObjectReference> ObjectReferences { get; init; } = Array.Empty<Models.SqlObjectReference>();
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
        public required bool HasActualRows { get; init; }
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
        public bool? IsParallel { get; init; }
        public required string Warnings { get; init; }
        public string DiagnosticStatusText { get; init; } = string.Empty;
        public required string NodeSeverity { get; init; }
        public required string OperatorType { get; init; }
    }

    public sealed class PlanGraphNodeBuilderService
    {
        private readonly RuleEngine _ruleEngine;
        private readonly PlanGraphOperatorTypeService _operatorTypeService = new();
        private readonly PlanGraphRelOpDetailsService _relOpDetailsService = new();
        private readonly PlanGraphRuntimeCountersService _runtimeCountersService = new();
        private readonly PlanGraphWarningService _warningService = new();
        private readonly IPlanExecutionFactsReader? _executionFacts;
        private readonly IUnexpectedErrorReporter? _unexpectedErrors;

        public PlanGraphNodeBuilderService(IPlanExecutionFactsReader? executionFacts = null,
            IUnexpectedErrorReporter? unexpectedErrors = null, RuleEngine? ruleEngine = null)
        {
            _executionFacts = executionFacts;
            _unexpectedErrors = unexpectedErrors;
            _ruleEngine = ruleEngine ?? new RuleEngine(unexpectedErrors: unexpectedErrors);
            if (ruleEngine == null) _ruleEngine.RegisterDefaultRules();
        }

        public PlanGraphNodeBuildResult Build(
            XElement relOp,
            XNamespace ns,
            PlanGraphNodeWarningSettings warningSettings)
        {
            ArgumentNullException.ThrowIfNull(relOp);
            ArgumentNullException.ThrowIfNull(ns);
            ArgumentNullException.ThrowIfNull(warningSettings);
            try
            {
                return BuildCore(relOp, ns, warningSettings);
            }
            catch (Exception ex)
            {
                ExceptionPolicy.Describe(ex, "PlanGraphNodeBuilderService.Build", _unexpectedErrors);
                throw;
            }
        }

        private PlanGraphNodeBuildResult BuildCore(XElement relOp, XNamespace ns,
            PlanGraphNodeWarningSettings warningSettings)
        {
            var facts = PlanOperatorFactsService.Get(relOp, ns);
            PlanExecutionFacts executionFacts = _executionFacts?.Read(relOp, ns) ?? facts.Execution;
            var identity = PlanIdentityAdapter.GetOperator(relOp);

            string nodeId = relOp.Attribute("NodeId")?.Value ?? "?";
            string physical = relOp.Attribute("PhysicalOp")?.Value ?? relOp.Attribute("LogicalOp")?.Value ?? "Unknown";
            string logical = relOp.Attribute("LogicalOp")?.Value ?? "Unknown";

            double estimatedRows = facts.EstimatedRows.Value ?? 0;
            double estimatedRowsRead = facts.EstimatedRowsRead.Value ?? 0;
            double subtreeCost = facts.SubtreeCost.Value ?? 0;

            string estimatedIoCost = facts.EstimatedIoCost.Display();
            string estimatedCpuCost = facts.EstimatedCpuCost.Display();
            string estimatedExecutions = facts.EstimatedExecutions.Display("0.0");

            PlanGraphRuntimeCountersResult runtimeCounters =
                _runtimeCountersService.Parse(relOp, ns);
            PlanGraphRelOpDetails relOpDetails =
                _relOpDetailsService.Parse(relOp, ns, physical);
            string residualPredicate = string.Join(" AND ", relOpDetails.Predicates);
            string seekPredicate = string.Join(" AND ", relOpDetails.SeekPredicates);

            double estimatedRowSizeNumber = facts.AverageRowSize.Value ?? 0;
            double estimatedCpuNumber = facts.EstimatedCpuCost.Value ?? 0;
            double estimatedIoNumber = facts.EstimatedIoCost.Value ?? 0;
            double estimatedDataSizeMB = (estimatedRows * estimatedRowSizeNumber) / (1024.0 * 1024.0);
            double actualDataSizeMB = runtimeCounters.HasActual
                ? (runtimeCounters.ActualRows * estimatedRowSizeNumber) / (1024.0 * 1024.0)
                : 0.0;
            string estimatedDataSize = FormatDataSizeMB(estimatedDataSizeMB);
            string actualDataSize = FormatDataSizeMB(actualDataSizeMB);

            PlanDiagnosticReport diagnostics = _ruleEngine.AnalyzeNodeDetailed(relOp, ns);
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
                    diagnostics.ToLegacyResults(), diagnostics);

            double ownCost = facts.OwnCost.Value ?? 0;
            double actualRecost = CalculateActualRecost(
                ownCost,
                estimatedRows,
                runtimeCounters.ActualRows,
                runtimeCounters.HasActual);

            return new PlanGraphNodeBuildResult
            {
                RawElement = relOp,
                Facts = facts,
                Diagnostics = diagnostics,
                NodeId = nodeId,
                Identity = identity?.Key,
                SourceLocation = identity?.Location,
                ObjectReferences = identity?.Objects ?? Array.Empty<Models.SqlObjectReference>(),
                PhysicalOp = physical,
                LogicalOp = logical,
                ExecutionMode = executionFacts.ExecutionMode,
                Cost = ownCost,
                OwnCost = ownCost,
                ActualRecost = actualRecost,
                SubtreeCost = subtreeCost,
                CostPercent = 1,
                EstRows = facts.EstimatedRows.IsAvailable ? PlanGraphMetricService.FormatNumber(estimatedRows) : "N/A",
                EstRowsNum = estimatedRows,
                EstimatedRowsToBeRead = facts.EstimatedRowsRead.IsAvailable ? PlanGraphMetricService.FormatNumber(estimatedRowsRead) : "N/A",
                EstimatedCPUCostNum = estimatedCpuNumber,
                EstimatedIOCostNum = estimatedIoNumber,
                AvgRowSizeNum = estimatedRowSizeNumber,
                EstimatedIOCost = estimatedIoCost,
                EstimatedCPUCost = estimatedCpuCost,
                EstimatedExecutions = estimatedExecutions,
                ActualExecutions = facts.ThreadExecutions.Display("N0"),
                ActualRows = runtimeCounters.ActualRowsDisplay,
                ActualRowsRead = runtimeCounters.ActualRowsReadDisplay,
                HasActualRows = runtimeCounters.HasActual,
                ActualRowsNum = runtimeCounters.ActualRows,
                EstimatedOperatorCost = facts.OwnCost.Display("0.0000000"),
                EstimatedSubtreeCostStr = facts.SubtreeCost.Display("0.0000000"),
                EstimatedRowSize = facts.AverageRowSize.IsAvailable ? estimatedRowSizeNumber.ToString("0") + " B" : "N/A",
                EstimatedDataSize = facts.EstimatedRows.IsAvailable && facts.AverageRowSize.IsAvailable ? estimatedDataSize : "N/A",
                ActualDataSize = runtimeCounters.HasActual && facts.AverageRowSize.IsAvailable ? actualDataSize : "N/A",
                ActualRebinds = facts.Rebinds.Display(),
                ActualRewinds = facts.Rewinds.Display(),
                Ordered = executionFacts.OrderedDisplay,
                DatabaseName = identity == null ? relOpDetails.DatabaseName : identity.Objects.Count == 1 ? identity.Objects[0].Identity.Database ?? "" : "",
                TableName = identity == null ? relOpDetails.TableName : identity.Objects.Count == 1 ? identity.Objects[0].Identity.Object ?? "" : "",
                IndexName = identity == null ? relOpDetails.IndexName : identity.Objects.Count == 1 ? identity.Objects[0].Index ?? "" : "",
                SeekPredicates = string.Join("\n", relOpDetails.SeekPredicates),
                Predicate = string.Join("\n", relOpDetails.Predicates),
                OutputList = string.Join(", ", relOpDetails.OutputColumns),
                ObjectDetails = identity == null ? relOpDetails.ObjectDetails : string.Join("; ", identity.Objects.Select(obj => obj.DisplayName)),
                Partitioned = facts.Partitioned?.ToString() ?? "N/A",
                PartitionCount = relOpDetails.PartitionCount,
                PartitionRange = relOpDetails.PartitionRange,
                IsParallel = executionFacts.Parallel,
                Warnings = warningResult.WarningsText,
                DiagnosticStatusText = warningResult.DiagnosticStatusText,
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
            if (!double.IsFinite(sizeMB)) return "N/A";
            return sizeMB < 1.0
                ? $"{sizeMB * 1024:F0} KB"
                : $"{sizeMB:F0} MB";
        }

    }
}
