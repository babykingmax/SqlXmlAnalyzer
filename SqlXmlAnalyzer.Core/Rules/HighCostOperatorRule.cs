using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class HighCostOperatorRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_022_HIGH_COST_OP";
        public string Name => "High Cost Operator Detection";
        public string Description => "Detects the top 5 operators with high individual resource costs.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            if (relOp.Attribute("NodeId")?.Value != "0") return null;

            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var allRelOps = doc.Descendants(ns + "RelOp");
                var details = new List<NodeDetail>();

                foreach (var r in allRelOps)
                {
                    if (r == null) continue;
                    string nodeId = r.Attribute("NodeId")?.Value ?? "?";
                    string physOp = r.Attribute("PhysicalOp")?.Value ?? "Unknown";
                    double subtreeCost = PlanDiagnosticAnalyzer.ParseDouble(r.Attribute("EstimatedTotalSubtreeCost")?.Value);

                    double ownCost = 0.0;
                    try
                    {
                        var childRelOps = PlanDiagnosticAnalyzer.GetDirectChildRelOps(r, ns);
                        double childrenCost = childRelOps.Select(c => PlanDiagnosticAnalyzer.ParseDouble(c?.Attribute("EstimatedTotalSubtreeCost")?.Value)).Sum();
                        ownCost = Math.Max(0.0, subtreeCost - childrenCost);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"NodeId {nodeId} 计算 OwnCost 异常: {ex.Message}");
                    }

                    details.Add(new NodeDetail
                    {
                        NodeId = nodeId,
                        PhysicalOp = physOp,
                        OwnCost = ownCost,
                        SubtreeCost = subtreeCost
                    });
                }

                var topNodes = details.OrderByDescending(n => n.OwnCost).Take(5).ToList();
                var messages = new List<string>();

                foreach (var node in topNodes)
                {
                    if (node == null) continue;
                    if (node.OwnCost > 0.005)
                    {
                        messages.Add($"⏱️ 算子 Node {node.NodeId} ({node.PhysicalOp}): 独占单体硬件开销预估高达 {node.OwnCost:F4} (占该算子子树开销的 {(node.OwnCost / Math.Max(node.SubtreeCost, 0.001)) * 100.0:F1}%)。建议在此算子做重点定位。");
                    }
                }

                if (messages.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "高开销硬件算子 Top 5",
                        Message = string.Join("|||", messages),
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"HighCostOperatorRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
