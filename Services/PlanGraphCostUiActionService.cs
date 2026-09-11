using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanGraphCostUiActionService
    {
        private readonly Core.Services.PlanGraphCostCalculationService _costCalculationService = new();

        public void ApplyCostCalculations(
            IReadOnlyList<XElement> relOps,
            IReadOnlyDictionary<XElement, PlanNodeViewModel> nodeMap,
            XNamespace ns,
            DiagramViewMode initialView,
            PlanColorMode initialColor)
        {
            ArgumentNullException.ThrowIfNull(relOps);
            ArgumentNullException.ThrowIfNull(nodeMap);
            ArgumentNullException.ThrowIfNull(ns);

            List<Core.Services.PlanGraphNodeCostInput> inputs = new();

            foreach (XElement relOp in relOps)
            {
                PlanNodeViewModel vm = nodeMap[relOp];
                List<XElement> childRelOps =
                    PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns).ToList();
                vm.HasChildren = childRelOps.Count > 0;
                List<double> childSubtreeCosts = childRelOps
                    .Select(child =>
                    {
                        if (nodeMap.TryGetValue(child, out PlanNodeViewModel? childVm))
                        {
                            return childVm.SubtreeCost;
                        }

                        return Core.Services.PlanOperatorFactsService.Get(child, ns).SubtreeCost.Value ?? 0;
                    })
                    .ToList();

                inputs.Add(new Core.Services.PlanGraphNodeCostInput(
                    vm.SubtreeCost,
                    childSubtreeCosts,
                    vm.EstimatedCPUCostNum,
                    vm.EstimatedIOCostNum,
                    vm.EstRowsNum,
                    vm.ActualRowsNum,
                    vm.HasActualRows) { Facts = vm.Facts });
            }

            IReadOnlyList<Core.Services.PlanGraphNodeCostResult> results =
                _costCalculationService.Calculate(inputs);

            for (int i = 0; i < relOps.Count; i++)
            {
                PlanNodeViewModel vm = nodeMap[relOps[i]];
                Core.Services.PlanGraphNodeCostResult result = results[i];
                vm.OwnCost = result.OwnCost;
                vm.Cost = result.DisplayCost;
                vm.ActualRecost = result.ActualRecost;
                vm.CostPercent = result.CostPercent;
                vm.CpuPercent = result.CpuPercent;
                vm.IoPercent = result.IoPercent;
                vm.ViewMode = initialView;
                vm.ColorMode = initialColor;
            }

            Logger.Debug($"IMP-07: 成本重算完成；节点数={inputs.Count}，实际行数已知={inputs.Count(node => node.HasActualRows)}。");
        }

    }
}
