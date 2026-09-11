using System;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphNodeClipboardUiActionService
    {
        private readonly Core.Services.PlanGraphNodeClipboardService _clipboardService = new();

        public string BuildNodeInfo(PlanNodeViewModel node)
        {
            ArgumentNullException.ThrowIfNull(node);

            return _clipboardService.BuildNodeInfo(ToClipboardInfo(node));
        }

        private static Core.Services.PlanGraphNodeClipboardInfo ToClipboardInfo(
            PlanNodeViewModel node)
        {
            return new Core.Services.PlanGraphNodeClipboardInfo(
                node.NodeId,
                node.PhysicalOp,
                node.LogicalOp,
                node.Facts?.SubtreeCost.IsAvailable == true ? node.Facts.SubtreeCost.Value : null,
                node.Facts?.SubtreeCost.IsAvailable == true ? node.CostPercent : null,
                node.EstRows,
                node.ActualRows,
                node.EstimatedDataSize,
                node.ObjectDetails,
                node.OutputList,
                node.SeekPredicates,
                node.Predicate,
                node.Warnings);
        }
    }
}
