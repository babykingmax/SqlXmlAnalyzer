using System;
using System.Windows;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphNodeUiActionService
    {
        private readonly Core.Services.PlanGraphNodeBuilderService _nodeBuilderService = new();

        public PlanNodeViewModel CreateNodeFromRelOp(
            XElement relOp,
            XNamespace ns,
            double residualIoThreshold,
            int residualIoMinRowsRead)
        {
            ArgumentNullException.ThrowIfNull(relOp);
            ArgumentNullException.ThrowIfNull(ns);

            Core.Services.PlanGraphNodeBuildResult node =
                _nodeBuilderService.Build(
                    relOp,
                    ns,
                    new Core.Services.PlanGraphNodeWarningSettings(
                        residualIoThreshold,
                        residualIoMinRowsRead));

            var vm = new PlanNodeViewModel
            {
                RawElement = node.RawElement,
                NodeId = node.NodeId,
                PhysicalOp = node.PhysicalOp,
                LogicalOp = node.LogicalOp,
                ExecutionMode = node.ExecutionMode,
                Cost = node.Cost,
                OwnCost = node.OwnCost,
                ActualRecost = node.ActualRecost,
                SubtreeCost = node.SubtreeCost,
                CostPercent = node.CostPercent,
                EstRows = node.EstRows,
                EstRowsNum = node.EstRowsNum,
                EstimatedRowsToBeRead = node.EstimatedRowsToBeRead,
                EstimatedCPUCostNum = node.EstimatedCPUCostNum,
                EstimatedIOCostNum = node.EstimatedIOCostNum,
                AvgRowSizeNum = node.AvgRowSizeNum,
                EstimatedIOCost = node.EstimatedIOCost,
                EstimatedCPUCost = node.EstimatedCPUCost,
                EstimatedExecutions = node.EstimatedExecutions,
                ActualExecutions = node.ActualExecutions,
                ActualRows = node.ActualRows,
                ActualRowsRead = node.ActualRowsRead,
                ActualRowsNum = node.ActualRowsNum,
                EstimatedOperatorCost = node.EstimatedOperatorCost,
                EstimatedSubtreeCostStr = node.EstimatedSubtreeCostStr,
                EstimatedRowSize = node.EstimatedRowSize,
                EstimatedDataSize = node.EstimatedDataSize,
                ActualDataSize = node.ActualDataSize,
                ActualRebinds = node.ActualRebinds,
                ActualRewinds = node.ActualRewinds,
                Ordered = node.Ordered,
                DatabaseName = node.DatabaseName,
                TableName = node.TableName,
                IndexName = node.IndexName,
                SeekPredicates = node.SeekPredicates,
                Predicate = node.Predicate,
                OutputList = node.OutputList,
                ObjectDetails = node.ObjectDetails,
                Partitioned = node.Partitioned,
                PartitionCount = node.PartitionCount,
                PartitionRange = node.PartitionRange,
                IsParallel = node.IsParallel,
                Warnings = node.Warnings,
                NodeSeverity = node.NodeSeverity,
                OperatorType = node.OperatorType,
                Location = new Point(50, 50)
            };

            var iconInfo = PhysicalOpToIconMapper.Map(node.PhysicalOp);
            vm.IconGeometry = iconInfo.Geometry;
            vm.IconBrush = iconInfo.Brush;

            return vm;
        }
    }
}
