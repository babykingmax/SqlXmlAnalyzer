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
            var doc = relOp.Document;
            if (doc == null) return null;

            var allRelOps = doc.Descendants(ns + "RelOp");
            var details = new List<NodeDetail>();

            foreach (var r in allRelOps)
            {
                if (r == null) continue;
                string nodeId = r.Attribute("NodeId")?.Value ?? "?";
                string physOp = r.Attribute("PhysicalOp")?.Value ?? "Unknown";
                var facts = Services.PlanOperatorFactsService.Get(r, ns);
                if (!facts.OwnCost.IsAvailable || !facts.SubtreeCost.IsAvailable) continue;
                double subtreeCost = facts.SubtreeCost.Value!.Value;
                double ownCost = facts.OwnCost.Value!.Value;

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

            return null;
        }
    }
}
