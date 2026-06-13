// =====================================================================================
// ExecutionPlanVisualizer.cs - 执行计划操作符树 Mermaid 可视化
// 类似死锁的 Wait-For Graph 思路，将执行计划树可视化展示
// 重点突出：高成本算子、扫描操作、并行度、缺失索引位置
// =====================================================================================

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    internal static class ExecutionPlanVisualizer
    {
        /// <summary>
        /// 为执行计划生成 Mermaid flowchart（操作符树）
        /// </summary>
        public static string GenerateMermaidPlan(XDocument doc, XNamespace ns)
        {
            if (doc?.Root == null) return "flowchart TD\n    A[无效的执行计划]";

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("flowchart TD");
                sb.AppendLine("    %% SqlXmlAnalyzer 自动生成的执行计划可视化");
                sb.AppendLine("    %% 推荐复制到 https://mermaid.live 查看");

                // 找到所有 QueryPlan（可能有多个语句）
                var queryPlans = doc.Descendants(ns + "QueryPlan").ToList();

                if (queryPlans.Count == 0)
                {
                    sb.AppendLine("    A[未找到 QueryPlan 节点]");
                    return sb.ToString();
                }

                int nodeCounter = 0;
                var nodeIdMap = new Dictionary<XElement, string>(); // RelOp -> Mermaid ID

                // 取第一个 QueryPlan 作为主展示（可扩展支持多个）
                var mainPlan = queryPlans.First();
                var rootRelOp = mainPlan.Element(ns + "RelOp") ?? mainPlan.Descendants(ns + "RelOp").FirstOrDefault();

                if (rootRelOp == null)
                {
                    sb.AppendLine("    A[未找到根 RelOp]");
                    return sb.ToString();
                }

                // 递归构建树
                BuildOperatorTree(rootRelOp, ns, sb, ref nodeCounter, nodeIdMap, "", true);

                // 添加样式定义
                sb.AppendLine();
                // === Plan Explorer 风格的颜色系统 ===
                sb.AppendLine("    classDef expensive fill:#FFCDD2,stroke:#D32F2F,stroke-width:3px,color:#B71C1C");   // 高成本 - 醒目红色
                sb.AppendLine("    classDef scan fill:#FFE0B2,stroke:#F57C00,stroke-width:2px");                       // 扫描警告 - 橙色
                sb.AppendLine("    classDef seek fill:#C8E6C9,stroke:#388E3C,stroke-width:2px");                       // Seek - 健康绿色
                sb.AppendLine("    classDef parallelism fill:#BBDEFB,stroke:#1976D2,stroke-width:2px");                // 并行 - 蓝色
                sb.AppendLine("    classDef normal fill:#FFFFFF,stroke:#9E9E9E,stroke-width:1px");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogException("ExecutionPlanVisualizer.GenerateMermaidPlan", ex);
                return $"flowchart TD\n    A[生成 Mermaid 执行计划图时发生错误: {ex.Message}]";
            }
        }

        private static string BuildOperatorTree(
            XElement relOp,
            XNamespace ns,
            StringBuilder sb,
            ref int nodeCounter,
            Dictionary<XElement, string> nodeIdMap,
            string parentId,
            bool isRoot)
        {
            if (relOp == null) return parentId ?? "";

            nodeCounter++;
            string nodeId = $"op{nodeCounter}";
            nodeIdMap[relOp] = nodeId;

            try
            {
                string physical = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";
                string logical = relOp.Attribute("LogicalOp")?.Value ?? "";
                string costStr = relOp.Attribute("EstimatedTotalSubtreeCost")?.Value ?? "0";
                string estRows = relOp.Attribute("EstimatedRows")?.Value ?? "?";

                double cost = 0;
                double.TryParse(costStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out cost);

                // === Plan Explorer 风格的丰富 node 标签 ===
                string label = physical;
                if (!string.IsNullOrEmpty(logical) && logical != physical)
                    label += $" ({logical})";

                // 成本和行数（核心 Plan Explorer 显示项）
                label += $"\\nCost: {costStr} | Est Rows: {estRows}";

                // 实际行数（如果有，Plan Explorer 非常重视 Actual vs Estimated）
                var actualRows = relOp.Attribute("ActualRows")?.Value;
                if (!string.IsNullOrEmpty(actualRows))
                    label += $" | Actual: {actualRows}";

                // 对象信息（表/索引）- Plan Explorer 经典显示
                var obj = relOp.Descendants(ns + "Object").FirstOrDefault();
                if (obj != null)
                {
                    string table = obj.Attribute("Table")?.Value?.Trim('[', ']') ?? "";
                    string index = obj.Attribute("Index")?.Value?.Trim('[', ']') ?? "";
                    string alias = obj.Attribute("Alias")?.Value ?? "";

                    if (!string.IsNullOrEmpty(table))
                    {
                        label += $"\\n{table}";
                        if (!string.IsNullOrEmpty(index))
                            label += $" [{index}]";
                        if (!string.IsNullOrEmpty(alias))
                            label += $" AS {alias}";
                    }
                }

                label = EscapeMermaidLabel(label);

                // === 增强样式（更接近 Plan Explorer） ===
                string className = ":::normal";
                string physicalLower = physical.ToLowerInvariant();

                if (cost > 5.0 || (Core.NumericParser.TryParseInvariantDouble(costStr, out double c) && c > 10))
                    className = ":::expensive";                    // 高成本 - 红色
                else if (physicalLower.Contains("scan") && !physicalLower.Contains("seek"))
                    className = ":::scan";                         // 扫描 - 橙色警告
                else if (physicalLower.Contains("seek"))
                    className = ":::seek";                         // Seek - 绿色
                else if (physicalLower.Contains("parallelism") || physicalLower.Contains("exchange"))
                    className = ":::parallelism";                  // 并行 - 蓝色

                // 额外：Key Lookup / Bookmark Lookup 特殊高亮（死锁风险）
                if (physicalLower.Contains("key lookup") || physicalLower.Contains("bookmark"))
                    className = ":::expensive";

                sb.AppendLine($"    {nodeId}[\"{label}\"]{className}");

                // 如果有父节点，画边
                if (!string.IsNullOrEmpty(parentId))
                {
                    sb.AppendLine($"    {nodeId} --> {parentId}"); // 数据流向：子节点指向父节点
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"BuildOperatorTree 节点 {nodeId} 构建失败: {ex.Message}");
                sb.AppendLine($"    {nodeId}[\"Error: {EscapeMermaidLabel(ex.Message)}\"]:::expensive");
                if (!string.IsNullOrEmpty(parentId))
                {
                    sb.AppendLine($"    {nodeId} --> {parentId}");
                }
            }

            // 递归处理子 RelOp - 使用 PlanDiagnosticAnalyzer 解决嵌套包裹元素问题
            try
            {
                var childRelOps = PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns);
                if (childRelOps != null)
                {
                    foreach (var child in childRelOps)
                    {
                        if (child == null) continue;
                        try
                        {
                            BuildOperatorTree(child, ns, sb, ref nodeCounter, nodeIdMap, nodeId, false);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning($"BuildOperatorTree 递归子节点失败: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"获取节点 {nodeId} 的子节点失败: {ex.Message}");
            }

            return nodeId;
        }

        private static string EscapeMermaidLabel(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            try
            {
                // 转义双引号
                text = text.Replace("\"", "\\\"");

                // 替换不合法字符
                var sb = new StringBuilder();
                foreach (char c in text)
                {
                    if (c == '\r' || c == '\n')
                    {
                        sb.Append("\\n");
                    }
                    else if (char.IsLetterOrDigit(c) || " _-.,()[]\\/|:+=%*?&@#$!<>".Contains(c))
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append('_');
                    }
                }
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 生成执行计划的简化文字树（作为 Mermaid 的补充）
        /// </summary>
        public static string GenerateTextTree(XDocument doc, XNamespace ns)
        {
            if (doc?.Root == null) return "未找到执行计划";

            try
            {
                var sb = new StringBuilder();
                var queryPlans = doc.Descendants(ns + "QueryPlan").ToList();

                if (queryPlans.Count == 0) return "未找到执行计划";

                foreach (var qp in queryPlans.Take(1))
                {
                    if (qp == null) continue;
                    var root = qp.Element(ns + "RelOp") ?? qp.Descendants(ns + "RelOp").FirstOrDefault();
                    if (root != null)
                    {
                        PrintRelOpTree(root, ns, sb, 0);
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogException("ExecutionPlanVisualizer.GenerateTextTree", ex);
                return $"生成文本树时发生错误: {ex.Message}";
            }
        }

        private static void PrintRelOpTree(XElement relOp, XNamespace ns, StringBuilder sb, int depth)
        {
            if (relOp == null || sb == null) return;

            try
            {
                string indent = new string(' ', Math.Max(0, depth * 2));
                string phys = relOp.Attribute("PhysicalOp")?.Value ?? "?";
                string cost = relOp.Attribute("EstimatedTotalSubtreeCost")?.Value ?? "0";

                sb.AppendLine($"{indent}- {phys} (Cost: {cost})");

                var children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(relOp, ns);
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child == null) continue;
                        try
                        {
                            PrintRelOpTree(child, ns, sb, depth + 1);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning($"PrintRelOpTree 递归子节点失败: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"PrintRelOpTree 遍历节点失败: {ex.Message}");
            }
        }
    }
}
