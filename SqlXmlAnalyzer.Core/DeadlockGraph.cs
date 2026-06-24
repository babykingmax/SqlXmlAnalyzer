// =====================================================================================
// DeadlockGraph.cs - Wait-For Graph 建模 + 死锁环检测 + Mermaid 可视化
// 目标：将死锁从“文本列表”升级为“可直观理解的等待图 + 环路可视化”
// =====================================================================================

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    // ==================== 死锁分析核心数据模型（从 Program.cs 提取，便于共享） ====================
    public sealed record DeadlockProcess(
        string Id,
        string Spid,
        string Loginname,
        string Hostname,
        string Isolationlevel,
        string Status,
        string Inputbuf,
        List<ExecutionFrame> ExecutionStack,
        string TransactionName = "",
        string CurrentDbName = "",
        string ClientApp = "",
        string WaitResource = "",
        string WaitTime = "",
        string Ecid = "",
        string DeadlockPriority = "0",
        string LogUsed = "0"
    );

    public sealed record ExecutionFrame(
        string Procname,
        string Line,
        string Statement
    );

    public sealed record LockResource(
        string LockType,
        string ObjectName,
        string IndexName,
        string Hobtid,
        string Dbid,
        List<LockOwner> Owners,
        List<LockWaiter> Waiters,
        string Id = ""
    )
    {
        public string CleanTableName => string.IsNullOrEmpty(ObjectName) ? "(Unknown)" :
            (ObjectName.Contains(".") ? ObjectName.Split('.').Last() : ObjectName);

        public string OwnerModes => string.Join(", ", Owners.Select(o => o.Mode));
        public string WaiterModes => string.Join(", ", Waiters.Select(w => w.Mode));
        public string ConflictSummary => $"Own({OwnerModes}) ➔ Req({WaiterModes})";
    }

    public sealed record LockOwner(string Id, string Mode);
    public sealed record LockWaiter(string Id, string Mode, string RequestType);

    public sealed record SargWarning(
        string Title,
        string Desc,
        string Solution
    );

    public static partial class SargAnalyzer
    {
        // 编译期生成正则表达式，大幅提升运行时的匹配和解析性能
        [System.Text.RegularExpressions.GeneratedRegex(@"--.*$", System.Text.RegularExpressions.RegexOptions.Multiline)]
        private static partial System.Text.RegularExpressions.Regex LineCommentRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"/\*[\s\S]*?\*/")]
        private static partial System.Text.RegularExpressions.Regex BlockCommentRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
        private static partial System.Text.RegularExpressions.Regex WhitespaceRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"\bLIKE\s+N?['""]%", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
        private static partial System.Text.RegularExpressions.Regex LeadingWildcardRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"\b(YEAR|MONTH|DAY|DATEPART|DATEDIFF|DATEADD|CONVERT|CAST|ISNULL|COALESCE|SUBSTRING|LEFT|RIGHT|UPPER|LOWER|RTRIM|LTRIM|LEN|CHARINDEX|PATINDEX)\s*\(([^()]*(?:\([^()]*\)[^()]*)*)\)\s*(?:>=|<=|=|!=|<>|>|<|IN\b|LIKE\b|IS\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
        private static partial System.Text.RegularExpressions.Regex ScalarFunctionRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"'[^']*'|@\w+|\b\d+(\.\d+)?\b|\b(varchar|nvarchar|char|nchar|int|bigint|datetime|date|dd|mm|yyyy|as)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
        private static partial System.Text.RegularExpressions.Regex ConstantAndTypeRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"[a-zA-Z_]")]
        private static partial System.Text.RegularExpressions.Regex IdentifierRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"(\bNOT\s+IN\s*\(|!=|<>)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
        private static partial System.Text.RegularExpressions.Regex NegativeQueryRegex();

        private static string StripComments(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return "";
            try
            {
                var s = LineCommentRegex().Replace(sql, "");
                s = BlockCommentRegex().Replace(s, "");
                return s.Trim();
            }
            catch (Exception ex)
            {
                Logger.Warning($"StripComments: 清除 SQL 注释时发生异常，返回原始文本: {ex.Message}");
                return sql.Trim();
            }
        }

        public static List<SargWarning> Analyze(string sql)
        {
            var warnings = new List<SargWarning>();
            string cleanSql = StripComments(sql);
            if (string.IsNullOrEmpty(cleanSql) || cleanSql.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return warnings;

            string flatSql = WhitespaceRegex().Replace(cleanSql, " ");

            // 1. 前导模糊查询
            if (LeadingWildcardRegex().IsMatch(flatSql))
            {
                warnings.Add(new SargWarning(
                    "🚫 前导模糊查询导致索引失效",
                    "在 WHERE 条件中检测到了 LIKE '%...'，这种前导模糊匹配会使 SQL Server 无法执行高效的索引寻检 (Index Seek)，被迫退化为全表/全索引扫描，在大表上极易造成大范围锁定，诱发死锁。",
                    "修改为后缀匹配（如 LIKE 'ABC%'）以使索引寻检生效，或者在数据库中引入全文索引（Full-Text Index）。"
                ));
            }

            // 2. 索引列上的标量函数计算
            var matches = ScalarFunctionRegex().Matches(flatSql);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string funcName = match.Groups[1].Value.ToUpperInvariant();
                string args = match.Groups[2].Value.Trim();

                string clearedArgs = ConstantAndTypeRegex().Replace(args, "");
                if (IdentifierRegex().IsMatch(clearedArgs))
                {
                    warnings.Add(new SargWarning(
                        $"🚫 索引列函数致盲 ({funcName})",
                        $"在列 [{args}] 上使用了标量函数 [{funcName}] 进行计算。在 WHERE 条件的索引列上包围任何函数计算，都会使该列上的索引完全失效并退化为全表扫描，极大增加了锁定范围和并发死锁率。",
                        "利用代数原理进行等值改写，将计算转移到等号/比较符的右侧。例如，将 'YEAR(Birthday) = 2026' 改写为 'Birthday >= '2026-01-01' AND Birthday < '2027-01-01''。"
                    ));
                }
            }

            // 3. 负向查询风险
            if (NegativeQueryRegex().IsMatch(flatSql))
            {
                warnings.Add(new SargWarning(
                    "⚠️ 负向查询风险 (Not-SARGable)",
                    "在 WHERE 条件中检测到使用了负向查询操作符（如 !=, <>, 或 NOT IN）。负向操作符通常无法利用索引寻检，极易由于全表/全索引扫描引发范围锁竞争。",
                    "尽量将其转化为正向查询。例如，将 'Status != 'Deleted'' 转化为 'Status IN ('Active', 'Pending', 'Suspended')'，或者利用覆盖索引进行优化。"
                ));
            }

            return warnings.GroupBy(w => w.Title).Select(g => g.First()).ToList();
        }
    }

    /// <summary>
    /// 表示一次死锁事件中的等待图（Wait-For Graph）
    /// </summary>
    public sealed class DeadlockGraph
    {
        /// <summary>
        /// 所有参与死锁的进程
        /// </summary>
        public List<DeadlockProcess> Processes { get; } = new();

        /// <summary>
        /// 所有涉及的资源
        /// </summary>
        public List<LockResource> Resources { get; } = new();

        /// <summary>
        /// 等待边列表：From（等待者） → To（持有者）
        /// </summary>
        public List<WaitForEdge> Edges { get; } = new();

        /// <summary>
        /// 检测到的死锁环路（通常只有 1 个主环）
        /// </summary>
        public List<DeadlockCycle> Cycles { get; } = new();

        /// <summary>
        /// 死锁受害者进程 ID
        /// </summary>
        public string VictimProcessId { get; set; }

        /// <summary>
        /// 是否成功构建了有效的等待图
        /// </summary>
        public bool IsValid => Processes.Count > 0 && Edges.Count > 0;
    }

    /// <summary>
    /// 等待边：一个进程正在等待另一个进程持有的资源
    /// </summary>
    public sealed class WaitForEdge
    {
        /// <summary>
        /// 等待者进程 ID
        /// </summary>
        public string FromProcessId { get; set; }

        /// <summary>
        /// 被等待者（持有者）进程 ID
        /// </summary>
        public string ToProcessId { get; set; }

        /// <summary>
        /// 涉及的资源（用于展示）
        /// </summary>
        public LockResource Resource { get; set; }

        /// <summary>
        /// 等待者请求的锁模式
        /// </summary>
        public string RequestedMode { get; set; }

        /// <summary>
        /// 持有者当前持有的锁模式
        /// </summary>
        public string HeldMode { get; set; }

        public override string ToString()
            => $"{FromProcessId} → {ToProcessId} (Resource: {Resource?.ObjectName}, Wait:{RequestedMode}, Hold:{HeldMode})";
    }

    /// <summary>
    /// 一个死锁环路（循环等待链）
    /// </summary>
    public sealed class DeadlockCycle
    {
        public List<string> ProcessIds { get; set; } = new();
        public List<WaitForEdge> EdgesInCycle { get; set; } = new();

        public int Length => ProcessIds.Count;

        public string GetCycleDescription()
        {
            if (ProcessIds.Count == 0) return "无环路";
            return string.Join(" → ", ProcessIds) + " → " + ProcessIds[0];
        }
    }

    /// <summary>
    /// 等待图构建器 + 环路检测 + Mermaid 生成器
    /// </summary>
    public static class DeadlockGraphBuilder
    {
        /// <summary>
        /// 从已解析的死锁数据构建完整的 Wait-For Graph
        /// </summary>
        public static DeadlockGraph Build(
            List<DeadlockProcess> processes,
            List<LockResource> resources,
            string victimProcessId)
        {
            Logger.Info($"DeadlockGraphBuilder.Build: 开始构建 Wait-For Graph | 进程数: {processes?.Count ?? 0}, 资源数: {resources?.Count ?? 0}, 受害者: {victimProcessId}");

            if (processes == null) throw new ArgumentNullException(nameof(processes));
            if (resources == null) throw new ArgumentNullException(nameof(resources));

            var graph = new DeadlockGraph
            {
                VictimProcessId = victimProcessId ?? ""
            };

            // 复制进程和资源（浅拷贝引用即可）
            graph.Processes.AddRange(processes);
            graph.Resources.AddRange(resources);

            // 核心：构建等待边（独立 try-catch ，避免影响后续环路检测）
            try
            {
                BuildWaitForEdges(graph);
                Logger.Debug($"DeadlockGraphBuilder.Build: 等待边构建完成，共 {graph.Edges.Count} 条边");
            }
            catch (Exception ex)
            {
                Logger.LogException("DeadlockGraphBuilder.BuildWaitForEdges", ex);
            }

            // 检测死锁环路（独立 try-catch ，避免 DFS 崩溃导致整个分析失败）
            try
            {
                graph.Cycles.AddRange(DetectCycles(graph));
                Logger.Info($"DeadlockGraphBuilder.Build: 环路检测完成，共发现 {graph.Cycles.Count} 个死锁环");
            }
            catch (Exception ex)
            {
                Logger.LogException("DeadlockGraphBuilder.DetectCycles", ex);
            }

            return graph;
        }

        /// <summary>
        /// 构建等待边（Wait-For 关系）
        /// 规则：对于同一个资源，等待者(Waiter) 正在等待 持有者(Owner) 释放资源
        /// </summary>
        private static void BuildWaitForEdges(DeadlockGraph graph)
        {
            int edgeCount = 0;
            foreach (var resource in graph.Resources)
            {
                try
                {
                    // 防御性 null 检查（records 默认应初始化，但外部数据可能游离）
                    if (resource == null) continue;
                    if (resource.Waiters == null || resource.Owners == null) continue;
                    if (resource.Waiters.Count == 0 || resource.Owners.Count == 0)
                        continue;

                    foreach (var waiter in resource.Waiters)
                    {
                        if (waiter == null || string.IsNullOrEmpty(waiter.Id)) continue;
                        foreach (var owner in resource.Owners)
                        {
                            if (owner == null || string.IsNullOrEmpty(owner.Id)) continue;
                            // 避免自环（理论上不应该出现）
                            if (waiter.Id == owner.Id)
                                continue;

                            var edge = new WaitForEdge
                            {
                                FromProcessId = waiter.Id,
                                ToProcessId = owner.Id,
                                Resource = resource,
                                RequestedMode = waiter.Mode ?? "",
                                HeldMode = owner.Mode ?? ""
                            };

                            graph.Edges.Add(edge);
                            edgeCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"BuildWaitForEdges: 处理资源 [{resource?.ObjectName}] 时发生异常，已跳过: {ex.Message}");
                }
            }
            Logger.Debug($"BuildWaitForEdges: 共构建 {edgeCount} 条等待边");
        }

        /// <summary>
        /// 使用 DFS 检测图中的所有环路（简化版，适合死锁场景）
        /// </summary>
        private static List<DeadlockCycle> DetectCycles(DeadlockGraph graph)
        {
            var cycles = new List<DeadlockCycle>();
            try
            {
                var visited = new HashSet<string>();
                var path = new List<string>();
                var pathEdges = new List<WaitForEdge>();

                var adj = BuildAdjacencyList(graph.Edges);
                Logger.Debug($"DetectCycles: 开始 DFS 环路检测 | 节点数: {graph.Processes.Count}, 邻接表大小: {adj.Count}");

                foreach (var process in graph.Processes)
                {
                    if (process == null || string.IsNullOrEmpty(process.Id)) continue;
                    if (!visited.Contains(process.Id))
                    {
                        try
                        {
                            DfsFindCycles(process.Id, adj, visited, path, pathEdges, cycles, graph);
                        }
                        catch (StackOverflowException)
                        {
                            Logger.Error($"DetectCycles: 对进程 {process.Id} 进行 DFS 时发生栈溢出，跳过（图可能过于复杂）");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning($"DetectCycles: 对进程 {process.Id} 进行 DFS 时发生异常，跳过: {ex.Message}");
                        }
                    }
                }

                // 去重（同一个环可能被多次发现）
                var uniqueCycles = cycles
                    .GroupBy(c => string.Join(",", c.ProcessIds.OrderBy(x => x)))
                    .Select(g => g.First())
                    .ToList();

                Logger.Debug($"DetectCycles: 共发现 {cycles.Count} 个原始环，去重后 {uniqueCycles.Count} 个唐一环");
                return uniqueCycles;
            }
            catch (Exception ex)
            {
                Logger.LogException("DeadlockGraphBuilder.DetectCycles", ex);
                return cycles;
            }
        }

        private static Dictionary<string, List<(string To, WaitForEdge Edge)>> BuildAdjacencyList(List<WaitForEdge> edges)
        {
            var adj = new Dictionary<string, List<(string, WaitForEdge)>>();

            foreach (var edge in edges)
            {
                // 防御性检查：忽略 null 属性的边
                if (edge == null || string.IsNullOrEmpty(edge.FromProcessId) || string.IsNullOrEmpty(edge.ToProcessId))
                    continue;

                if (!adj.ContainsKey(edge.FromProcessId))
                    adj[edge.FromProcessId] = new List<(string, WaitForEdge)>();

                adj[edge.FromProcessId].Add((edge.ToProcessId, edge));
            }

            return adj;
        }

        private static void DfsFindCycles(
            string current,
            Dictionary<string, List<(string To, WaitForEdge Edge)>> adj,
            HashSet<string> visited,
            List<string> path,
            List<WaitForEdge> pathEdges,
            List<DeadlockCycle> cycles,
            DeadlockGraph graph)
        {
            path.Add(current);
            visited.Add(current);

            if (adj.TryGetValue(current, out var neighbors))
            {
                foreach (var (next, edge) in neighbors)
                {
                    int index = path.IndexOf(next);

                    if (index != -1)
                    {
                        // 找到环
                        var cycleProcesses = path.Skip(index).ToList();
                        cycleProcesses.Add(next); // 闭合环

                        var cycleEdges = pathEdges.Skip(index).ToList();
                        cycleEdges.Add(edge);

                        cycles.Add(new DeadlockCycle
                        {
                            ProcessIds = cycleProcesses,
                            EdgesInCycle = cycleEdges
                        });
                    }
                    else if (!visited.Contains(next))
                    {
                        pathEdges.Add(edge);
                        DfsFindCycles(next, adj, visited, path, pathEdges, cycles, graph);
                        pathEdges.RemoveAt(pathEdges.Count - 1);
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            visited.Remove(current);
        }

        // ==================== Mermaid 可视化生成 ====================

        /// <summary>
        /// 生成 Mermaid flowchart 代码（推荐直接复制到 https://mermaid.live 查看）
        /// </summary>
        public static string GenerateMermaid(DeadlockGraph graph, bool highlightCycle = true)
        {
            if (graph == null)
            {
                Logger.Warning("GenerateMermaid: graph 为 null，返回安全占位符");
                return "graph TD\n    A[无效死锁数据]";
            }
            if (!graph.IsValid)
            {
                Logger.Warning($"GenerateMermaid: 图数据无效 (Processes={graph.Processes.Count}, Edges={graph.Edges.Count})，返回占位符");
                return "graph TD\n    A[无有效死锁数据]";
            }

            Logger.Debug($"GenerateMermaid: 开始生成 Mermaid 图 | 进程: {graph.Processes.Count}, 边: {graph.Edges.Count}, 环: {graph.Cycles.Count}");

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("flowchart TD");
                sb.AppendLine("    %% SqlXmlAnalyzer 自动生成的死锁等待图");
                sb.AppendLine("    %% 推荐复制到 https://mermaid.live 查看图形化效果");

                // 1. 定义所有进程节点
                foreach (var proc in graph.Processes)
                {
                    try
                    {
                        if (proc == null) continue;
                        string rawLabel = BuildProcessLabel(proc, graph.VictimProcessId);
                        string label = EscapeMermaidLabel(rawLabel);
                        string style = proc.Id == graph.VictimProcessId ? ":::victim" : ":::normal";
                        sb.AppendLine($"    {SanitizeId(proc.Id)}[\"{label}\"]{style}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"GenerateMermaid: 生成进程节点标签失败 [{proc?.Id}]: {ex.Message}");
                    }
                }

                // 2. 定义所有资源节点
                foreach (var res in graph.Resources)
                {
                    try
                    {
                        if (res == null) continue;
                        string objName = res.ObjectName ?? "未知资源";
                        if (!string.IsNullOrEmpty(res.IndexName))
                            objName += $" ({res.IndexName})";
                        string label = EscapeMermaidLabel("🗄️ " + objName);
                        sb.AppendLine($"    {SanitizeId(res.Id)}[\"{label}\"]:::resource");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"GenerateMermaid: 生成资源节点标签失败 [{res?.Id}]: {ex.Message}");
                    }
                }

                // 3. 定义样式
                sb.AppendLine();
                sb.AppendLine("    classDef victim fill:#ffcccc,stroke:#cc0000,stroke-width:3px,color:#000000");
                sb.AppendLine("    classDef normal fill:#e6f3ff,stroke:#0066cc,color:#000000");
                sb.AppendLine("    classDef resource fill:#fff2e6,stroke:#ff9933,stroke-width:2px,color:#000000");

                // 4. 绘制等待边 (Bipartite)
                sb.AppendLine();
                var cycleEdges = new HashSet<WaitForEdge>();

                if (highlightCycle && graph.Cycles.Count > 0)
                {
                    try
                    {
                        var mainCycle = graph.Cycles.OrderByDescending(c => c.Length).First();
                        cycleEdges = new HashSet<WaitForEdge>(mainCycle.EdgesInCycle);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"GenerateMermaid: 确定主环路失败: {ex.Message}");
                    }
                }

                foreach (var edge in graph.Edges)
                {
                    try
                    {
                        if (edge == null || edge.Resource == null) continue;
                        string from = SanitizeId(edge.FromProcessId);
                        string to = SanitizeId(edge.ToProcessId);
                        string resId = SanitizeId(edge.Resource.Id);

                        string reqMode = EscapeMermaidLabel(edge.RequestedMode ?? "Unknown");
                        string holdMode = EscapeMermaidLabel(edge.HeldMode ?? "Unknown");

                        string arrow1 = cycleEdges.Contains(edge) ? "==>" : "-->";
                        string arrow2 = cycleEdges.Contains(edge) ? "==>" : "-->";

                        // 请求边 (Process -> Resource)
                        sb.AppendLine($"    {from} {arrow1}|\"请求 {reqMode}\"| {resId}");

                        // 分配边 (Resource -> Process)
                        sb.AppendLine($"    {resId} {arrow2}|\"分配 {holdMode}\"| {to}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"GenerateMermaid: 生成边标签失败: {ex.Message}");
                    }
                }

                // 4. 添加环路说明
                if (graph.Cycles.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("    %% 死锁环路说明");
                    foreach (var cycle in graph.Cycles.Take(3))
                    {
                        if (cycle != null)
                            sb.AppendLine($"    %% 环路: {cycle.GetCycleDescription()}");
                    }
                }

                Logger.Debug($"GenerateMermaid: Mermaid 代码生成完成，共 {sb.Length} 字符");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogException("DeadlockGraphBuilder.GenerateMermaid", ex);
                return $"graph TD\n    A[生成 Mermaid 图时发生错误: {ex.Message}]";
            }
        }

        private static string BuildProcessLabel(DeadlockProcess proc, string victimId)
        {
            var sb = new StringBuilder();

            bool isVictim = proc.Id == victimId;
            if (isVictim)
                sb.Append("💀 ");

            sb.Append($"SPID:{proc.Spid}");

            if (!string.IsNullOrEmpty(proc.Loginname))
                sb.Append($"\\n{proc.Loginname}");

            // 添加对死锁优先级 (Deadlock Priority) 的高亮展示
            if (!string.IsNullOrEmpty(proc.DeadlockPriority) && proc.DeadlockPriority != "0")
                sb.Append($"\\n优先级: {proc.DeadlockPriority}");

            long logUsedBytes = 0;
            if (long.TryParse(proc.LogUsed, out long logUsed))
                logUsedBytes = logUsed;

            sb.Append($"\\n回滚代价: {logUsedBytes} 日志量");
            if (isVictim)
                sb.Append($"\\n(判定为牺牲品)");

            // 取 inputbuf 前 65 个字符作为关键 SQL 提示
            if (!string.IsNullOrWhiteSpace(proc.Inputbuf))
            {
                string sql = proc.Inputbuf.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
                if (sql.Length > 68)
                    sql = sql.Substring(0, 65) + "...";

                sb.Append($"\\n{sql}");
            }

            if (isVictim)
                sb.Append("\\n【受害者】");

            return sb.ToString();
        }

        private static string BuildEdgeLabel(WaitForEdge edge)
        {
            var res = edge?.Resource;
            string obj = res?.ObjectName ?? "未知资源";

            if (res != null && !string.IsNullOrEmpty(res.IndexName))
                obj += $" ({res.IndexName})";

            // 限制长度，避免标签过长导致 Mermaid 解析问题
            if (obj.Length > 50)
                obj = obj.Substring(0, 47) + "...";

            string mode = $"{edge?.RequestedMode ?? "未知"}←{edge?.HeldMode ?? "未知"}";

            return $"{obj}\\n{mode}";
        }

        private static string SanitizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "n";
            // 更严格地清理 Mermaid node ID，只保留字母、数字和下划线
            return System.Text.RegularExpressions.Regex.Replace(id, @"[^a-zA-Z0-9_]", "_");
        }

        /// <summary>
        /// 对 Mermaid 标签内容进行安全转义，防止 Syntax error
        /// 采用非常激进的策略：移除或转义所有可能导致 Mermaid 解析失败的字符
        /// </summary>
        private static string EscapeMermaidLabel(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // 1. 先处理换行
            text = text.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");

            // 2. 必须先转义反斜杠
            text = text.Replace("\\", "\\\\");

            // 3. 转义 Mermaid 特殊字符
            text = text.Replace("\"", "\\\"");   // 双引号
            text = text.Replace("`", "\\`");     // 反引号
            text = text.Replace("|", "\\|");     // 竖线（边标签关键）
            text = text.Replace(":", "\\:");     // 冒号有时会导致问题
            text = text.Replace(";", "\\;");     // 分号
            text = text.Replace(">", "\\>");     // 箭头相关
            text = text.Replace("<", "\\<");

            // 4. 移除或替换其他容易出问题的字符（激进模式）
            // 保留常见安全字符，其余替换为下划线或空格
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-' || c == '.' || c == ',' || c == '(' || c == ')' || c == '[' || c == ']' || c == '\\' || c == 'n')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');   // 用下换线替换危险字符
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成简洁的 ASCII 环路图（备用）
        /// </summary>
        public static string GenerateAsciiCycle(DeadlockGraph graph)
        {
            if (graph == null || graph.Cycles == null || graph.Cycles.Count == 0)
                return "未检测到明显环路（数据可能不完整或图为空）";

            try
            {
                var cycle = graph.Cycles.OrderByDescending(c => c?.Length ?? 0).FirstOrDefault();
                if (cycle == null || cycle.ProcessIds == null || cycle.ProcessIds.Count == 0)
                    return "未检测到有效环路信息。";

                var sb = new StringBuilder();

                sb.AppendLine("死锁环路：");
                sb.AppendLine();

                for (int i = 0; i < cycle.ProcessIds.Count - 1; i++)
                {
                    string from = cycle.ProcessIds[i];
                    string to = cycle.ProcessIds[i + 1];

                    if (from == null || to == null) continue;

                    var edge = cycle.EdgesInCycle?.FirstOrDefault(e => e != null && e.FromProcessId == from && e.ToProcessId == to);

                    sb.AppendLine($"  {from}");
                    sb.AppendLine($"      ↓ 等待资源: {edge?.Resource?.ObjectName ?? "未知资源"}");
                    sb.AppendLine($"      ↓ 请求模式: {edge?.RequestedMode ?? "未知模式"} (持有者持 {edge?.HeldMode ?? "未知模式"})");
                    sb.AppendLine($"  {to}");
                    sb.AppendLine();
                }

                // 闭环
                string lastProcess = cycle.ProcessIds.LastOrDefault();
                if (lastProcess != null)
                {
                    sb.AppendLine($"  {lastProcess}  ←── 形成环路");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogException("GenerateAsciiCycle", ex);
                return $"生成环路图时发生异常: {ex.Message}";
            }
        }
    }

    // =====================================================================================
    // 死锁类型自动识别（Deadlock Pattern Analyzer）
    // =====================================================================================

    /// <summary>
    /// 表示检测到的死锁模式
    /// </summary>
    public sealed record DeadlockPattern(
        string TypeName,           // 例如 "U-X 转换死锁"
        string Severity,           // High / Medium / Low
        string Description,
        string LikelyCause,
        string Recommendation
    );

    public static class DeadlockPatternAnalyzer
    {
        /// <summary>
        /// 基于 Wait-For Graph + 原始资源信息 + 锁模式，自动识别常见的死锁模式 ( 专家级诊断启发式 )
        /// </summary>
        public static List<DeadlockPattern> IdentifyPatterns(DeadlockGraph graph, XDocument originalDoc = null)
        {
            var patterns = new List<DeadlockPattern>();

            if (graph == null)
                return patterns;

            Logger.Info($"IdentifyPatterns: 开始分析死锁模式 | 进程数={graph.Processes?.Count ?? 0}, 边数={graph.Edges?.Count ?? 0}");

            try
            {
                // 1. 并行查询内部死锁 (Parallel Query Deadlock)
                try
                {
                    if (HasParallelDeadlock(graph, originalDoc))
                    {
                        patterns.Add(new DeadlockPattern(
                            "⚡ CXPACKET 并发/并行内部死锁 (Parallel Intra-Query Deadlock)",
                            "High",
                            "单查询被强制多线程引发的互锁。优先解决右侧的 SARG 代码缺陷，或者附加 `OPTION (MAXDOP 1)` 降级。",
                            "当优化器为低效的查询扫描选择多线程执行（exchangeEvent）时，极易因线程乱序消费或写入而互相阻塞。在执行计划中常见为高代价的全表扫描+并行操作。",
                            "1. 审查业务追溯窗口：如果是由于隐式转换、标量函数致盲导致的高代价查询，先修复 SARG 缺陷使查询走索引。\n2. 若业务无法修改代码，请考虑在全局降低 Cost Threshold for Parallelism 或给该查询强制施加 MAXDOP 1。"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HasParallelDeadlock 检测异常: {ex.Message}");
                }

                // 2. Bookmark / Key Lookup 死锁 ( 经典诊断模式 )
                try
                {
                    if (HasBookmarkLookupPattern(graph))
                    {
                        patterns.Add(new DeadlockPattern(
                            "[专家级诊断] Bookmark / Key Lookup 回表死锁 (Key Lookup Deadlock)",
                            "High",
                            "一个并发会话正通过非聚集索引检索并执行 Key Lookup 回表（聚集索引/堆），而另一个并发会话同时在更新这几行数据的基表。",
                            "非聚集索引（Non-Clustered Index）未完全覆盖查询所需的所有列，迫使 SQL Server 生成 Key Lookup 操作去 Clustered Index 或 Heap RID 中回表取数，从而与并发的 UPDATE/DELETE 产生了锁顺序交叉冲突（NCI -> Clustered vs Clustered -> NCI）。",
                            "1. 创建覆盖索引：将 Key Lookup 回表检索的额外列作为 INCLUDE 列加入到非聚集索引中，完全消除 Key Lookup 回表操作，即可从数学上彻底根治此死锁；\n2. 考虑重写查询或收窄事务空间。"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HasBookmarkLookupPattern 检测异常: {ex.Message}");
                }

                // 3. U-X / U-S 锁升级转换死锁 (Conversion Deadlock)
                try
                {
                    if (HasConversionDeadlock(graph))
                    {
                        patterns.Add(new DeadlockPattern(
                            "[专家级诊断] 锁升级转换死锁 (Conversion Deadlock)",
                            "High",
                            "两个或多个并发事务都在对同一批数据进行“先读后写”的操作，同时尝试将已经持有的共享锁（S）或更新锁（U）升级为排他锁（X）。",
                            "UPDATE/DELETE 语句在事务执行初期未声明排他意图，而是先以 S/U 锁检索。并发下多个会话同时持有了相互兼容的 S/U 锁，在它们同时尝试升级为相互排斥 of X 锁时瞬间卡死。",
                            "1. 在事务扫描阶段，对 SELECT 语句显式加上 (UPDLOCK) 提示，强制并发事务从一开始就获得更新锁，禁止其他会话插入中间兼容状态，实现串行化；\n2. 减小事务粒度，尽快提交事务。"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HasConversionDeadlock 检测异常: {ex.Message}");
                }

                // 4. Serializable 范围锁死锁 (Serializable Range Deadlock)
                try
                {
                    if (HasRangeLockPattern(graph))
                    {
                        patterns.Add(new DeadlockPattern(
                            "[专家级诊断] SERIALIZABLE 范围锁死锁 (Range Lock Deadlock)",
                            "High",
                            "会话的隔离级别被设为了 SERIALIZABLE（可串行化）或使用了 HOLDLOCK 提示，使得 SQL Server 启用了键范围锁（RangeS-S, RangeI-N）来防止幻读。",
                            "在高并发的 Serializable 级别下，并发事务试图在相同的索引键值范围内进行读取或插入。由于可串行化要求必须锁定整个扫描范围，极易由于范围锁冲突而死锁。",
                            "1. 如果业务允许，将事务隔离级别降低为 Read Committed (或在数据库中启用 RCSI 行版本控制)；\n2. 为 WHERE 范围条件创建高度精准的索引以收窄范围锁的空间，避免大范围扫描。"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HasRangeLockPattern 检测异常: {ex.Message}");
                }

                // 5. 物理页面分裂 / 页级热点竞争死锁 (Page Split / Hotspot Contention)
                try
                {
                    if (HasPageSplitPattern(graph))
                    {
                        patterns.Add(new DeadlockPattern(
                            "[专家级诊断] 页面分裂 / 页级热点竞争 (Page/RID Lock Contention)",
                            "Medium",
                            "死锁发生于 PAGE 页面级别或 RID 物理行位置，通常由高并发下的并发插入引起的数据页分裂（Page Split）或物理热点竞争引起。",
                            "并发 INSERT 导致聚集索引尾部或特定的 Index Page 迅速塞满，SQL Server 需要频繁分配新页面并进行物理页分裂，此时对页面加锁（PAGE IX/X）发生剧烈竞争导致死锁。",
                            "1. 在主索引上使用更合适的填充因子（Fill Factor，如 80~90）留出足够物理空隙，减少运行时的物理分裂页；\n2. 优化聚集索引的设计，避免大量单调递增键在高并发下涌入同一个尾部数据页；\n3. 考虑对高并发冲突的表使用 ROWLOCK 提示收窄锁粒度。"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HasPageSplitPattern 检测异常: {ex.Message}");
                }

                // 6. 外键约束 / 级联更新删除死锁 (Foreign Key Cascade Deadlock)
                try
                {
                    if (HasForeignKeyPattern(graph))
                    {
                        patterns.Add(new DeadlockPattern(
                            "[专家级诊断] 外键约束级联死锁 (Foreign Key Cascade Deadlock)",
                            "High",
                            "由于外键未建立对应索引，或启用了 ON UPDATE/DELETE CASCADE 级联操作，导致操作子表或主表时隐式锁定了关联另一张大表。",
                            "SQL Server 在进行外键一致性校验或级联操作时，由于关联列缺乏索引，必须对子表进行全表扫描加锁，导致锁范围迅速扩大并与主表更新发生交叉顺序死锁。",
                            "1. 务必为数据库中的所有外键列创建显式非聚集索引，确保外键检查和级联删除时可以通过 Seek 精准锁定极少数记录；\n2. 避免大批量的级联删除，将其拆分为小批次的分步操作。"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HasForeignKeyPattern 检测异常: {ex.Message}");
                }

                // 7. 热点资源竞争死锁 (Single Resource Contention)
                try
                {
                    if (HasHighContentionOnSingleResource(graph))
                    {
                        patterns.Add(new DeadlockPattern(
                            "[专家级诊断] 极高单资源排队竞争 (Hotspot Resource Contention)",
                            "Medium",
                            "多个进程同时竞争同一个极其狭窄的物理热点资源（通常是单个索引键或物理行）。",
                            "表设计缺陷或业务代码导致所有高频并发事务都在同时更新/删除一模一样的单行记录（如全局序列、系统计数器、或状态共享行）。",
                            "1. 将并发更新操作拆分，或在业务层引入分布式锁/排队机制进行削峰；\n2. 增加分区或细化锁粒度。"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HasHighContentionOnSingleResource 检测异常: {ex.Message}");
                }

                // 9. sp_BlitzLock 锁链深度解析 (Lock Chain Semantics)
                try
                {
                    if (graph.Edges != null && graph.Edges.Count > 0)
                    {
                        var chainInfos = new List<string>();
                        foreach (var edge in graph.Edges)
                        {
                            if (edge == null || edge.Resource == null) continue;
                            string resType = edge.Resource.LockType?.ToUpper() ?? "UNKNOWN";
                            string reqMode = edge.RequestedMode?.ToUpper() ?? "";
                            string heldMode = edge.HeldMode?.ToUpper() ?? "";

                            string resTranslate = resType switch
                            {
                                "KEY" => "索引键(KEY)",
                                "PAG" => "数据页(PAGE)",
                                "PAGE" => "数据页(PAGE)",
                                "RID" => "堆行记录(RID)",
                                "TAB" => "表(TABLE)",
                                "OBJECT" => "表对象(OBJECT)",
                                "HOBT" => "堆或B树(HOBT)",
                                _ => resType
                            };

                            string modeTranslate(string m) => m switch
                            {
                                "S" => "共享读(S)",
                                "X" => "排他写(X)",
                                "U" => "更新预备(U)",
                                "IS" => "意向共享读(IS)",
                                "IX" => "意向排他写(IX)",
                                "SIX" => "共享意向排他(SIX)",
                                _ => m
                            };

                            if (!string.IsNullOrEmpty(reqMode) && !string.IsNullOrEmpty(heldMode))
                            {
                                string chainMsg = $"• SPID {edge.FromProcessId} 请求 {modeTranslate(reqMode)} 锁被阻塞，因 SPID {edge.ToProcessId} 正持有互斥的 {modeTranslate(heldMode)} 锁 (在 {resTranslate} '{edge.Resource.CleanTableName}' 上)。";
                                if (!chainInfos.Contains(chainMsg)) chainInfos.Add(chainMsg);
                            }
                        }

                        if (chainInfos.Count > 0)
                        {
                            patterns.Add(new DeadlockPattern(
                                "[sp_BlitzLock 诊断] 细粒度锁链与资源冲突轨迹 (Lock Chain Details)",
                                "Info",
                                "展示底层的物理锁定顺序与冲突语义，帮助快速定位阻塞性质（读写阻塞、写写阻塞或意向升级）。",
                                string.Join("\n", chainInfos),
                                "1. 如果发现大量 S(共享读) 被 X(排他写) 阻塞，建议开启 RCSI（Read Committed Snapshot Isolation）以彻底消除读写冲突；\n2. 若为 U/X 交叉，优化事务内的修改顺序或尽早添加 UPDLOCK 提示。"
                            ));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"sp_BlitzLock 锁链分析异常: {ex.Message}");
                }

                // 8. 经典多表访问顺序交叉死锁 (Cyclic Deadlock)
                if (patterns.Count == 0)
                {
                    patterns.Add(new DeadlockPattern(
                        "[专家级诊断] 经典多表锁顺序交叉死锁 (Cyclic Deadlock)",
                        "Medium",
                        "不同的并发业务会话以不同的逻辑顺序更新或访问相同的多张数据库表。",
                        "会话 A 锁定了表 T1，然后尝试锁定表 T2；而会话 B 锁定了表 T2，然后尝试锁定表 T1。锁访问顺序不一致（T1->T2 vs T2->T1）是数据库中最经典的物理交叉等待死锁。",
                        "1. 强制应用层的所有事务按严格一致的表顺序（例如：永远先访问 T1、再访问 T2）执行更新和加锁操作，即可从设计上 100% 根绝此类死锁；\n2. 缩短事务范围，尽快提交事务。"
                    ));
                }
                // 10. 显式死锁优先级干预 (Deadlock Priority Analysis)
                try
                {
                    if (graph.Processes != null)
                    {
                        var nonZeroPriorities = graph.Processes.Where(p => p != null && !string.IsNullOrEmpty(p.DeadlockPriority) && p.DeadlockPriority != "0").ToList();
                        if (nonZeroPriorities.Count > 0)
                        {
                            var victim = graph.Processes.FirstOrDefault(p => p.Id == graph.VictimProcessId);
                            string victimPriority = victim?.DeadlockPriority ?? "0";
                            patterns.Add(new DeadlockPattern(
                                "[专家级诊断] 显式死锁优先级干预 (Deadlock Priority Analysis)",
                                "Info",
                                $"检测到事务使用了 SET DEADLOCK_PRIORITY 显式设置了死锁优先级。受害者进程 (SPID: {victim?.Spid ?? "Unknown"}) 的优先级为: {victimPriority}。",
                                "应用程序逻辑中显式调用了 SET DEADLOCK_PRIORITY。在发生死锁时，SQL Server 优化器总是优先牺牲优先级数值较小的进程。如果优先级相同，则牺牲回滚开销较小的进程。",
                                "检查应用逻辑，确保优先级分配符合业务重要性。建议后台报表/批处理任务设置为 LOW 或负数，核心交易流设置为 HIGH 或正数。"
                            ));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Deadlock Priority 分析异常: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("IdentifyPatterns", ex);
            }

            return patterns;
        }

        private static bool HasConversionDeadlock(DeadlockGraph graph)
        {
            if (graph == null || graph.Edges == null) return false;
            foreach (var edge in graph.Edges)
            {
                if (edge == null) continue;
                string req = edge.RequestedMode?.ToUpper() ?? "";
                string held = edge.HeldMode?.ToUpper() ?? "";

                if ((held.Contains("U") && (req.Contains("X") || req.Contains("U"))) ||
                    (held.Contains("S") && req.Contains("X")))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasBookmarkLookupPattern(DeadlockGraph graph)
        {
            if (graph == null || graph.Resources == null || graph.Processes == null) return false;

            var tableGroups = graph.Resources
                .Where(r => r != null && !string.IsNullOrEmpty(r.ObjectName))
                .GroupBy(r => r.ObjectName.ToLowerInvariant())
                .Where(g => g.Count() >= 2);

            foreach (var group in tableGroups)
            {
                var resources = group.ToList();

                bool hasNonClusteredKeyLock = resources.Any(r =>
                    r != null &&
                    r.LockType == "keylock" &&
                    !string.IsNullOrEmpty(r.IndexName) &&
                    !r.IndexName.ToLowerInvariant().Contains("clustered") &&
                    !r.IndexName.ToLowerInvariant().Contains("heap"));

                bool hasClusteredOrRid = resources.Any(r =>
                    r != null &&
                    (r.LockType == "keylock" || r.LockType == "ridlock" || r.LockType == "pagelock"));

                var distinctIndexes = resources
                    .Where(r => r != null && r.LockType == "keylock" && !string.IsNullOrEmpty(r.IndexName))
                    .Select(r => r.IndexName.ToLowerInvariant())
                    .Distinct()
                    .Count();

                if ((hasNonClusteredKeyLock && hasClusteredOrRid) || distinctIndexes >= 2)
                    return true;
            }

            foreach (var proc in graph.Processes)
            {
                if (proc == null) continue;
                string sql = proc.Inputbuf?.ToLowerInvariant() ?? "";
                if (sql.Contains("key lookup") || sql.Contains("bookmark") || (sql.Contains("select") && sql.Contains("from") && sql.Contains("where")))
                {
                    if (sql.Contains("update") || sql.Contains("delete"))
                        return true;
                }
            }

            return false;
        }

        private static bool HasRangeLockPattern(DeadlockGraph graph)
        {
            if (graph == null || graph.Processes == null || graph.Edges == null) return false;

            foreach (var proc in graph.Processes)
            {
                if (proc == null) continue;
                string iso = proc.Isolationlevel?.ToLower() ?? "";
                if (iso.Contains("serializable") || iso.Contains("repeatable"))
                    return true;
            }

            foreach (var edge in graph.Edges)
            {
                if (edge == null) continue;
                string req = edge.RequestedMode?.ToUpper() ?? "";
                string held = edge.HeldMode?.ToUpper() ?? "";
                if (req.Contains("RANGE") || held.Contains("RANGE"))
                    return true;
            }
            return false;
        }

        private static bool HasHighContentionOnSingleResource(DeadlockGraph graph)
        {
            if (graph == null || graph.Resources == null) return false;
            return graph.Resources.Any(r => r != null && r.Waiters != null && r.Waiters.Count >= 2);
        }

        private static bool HasParallelDeadlock(DeadlockGraph graph, XDocument doc)
        {
            if (graph == null || graph.Processes == null) return false;

            // 通过 ecid 检测
            bool hasEcidThread = graph.Processes.Any(p => p != null && !string.IsNullOrEmpty(p.Ecid) && p.Ecid != "0");

            // 同一个 SPID 出现了多次
            bool hasSharedSpid = graph.Processes.Where(p => p != null).GroupBy(p => p.Spid).Any(g => g.Count() >= 2);

            if (hasEcidThread || hasSharedSpid) return true;

            if (doc != null)
            {
                try
                {
                    var descendants = doc.Descendants().ToList();
                    bool hasExchange = descendants.Any(e => e != null && e.Name != null && e.Name.LocalName != null && e.Name.LocalName.Contains("exchange", StringComparison.OrdinalIgnoreCase));
                    bool hasParallelism = descendants.Any(e => e != null && e.Name != null && e.Name.LocalName != null && e.Name.LocalName.Contains("parallelism", StringComparison.OrdinalIgnoreCase));
                    if (hasExchange || hasParallelism) return true;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HasParallelDeadlock XDocument 检查抛出异常: {ex.Message}");
                }
            }

            return false;
        }

        private static bool HasPageSplitPattern(DeadlockGraph graph)
        {
            if (graph == null || graph.Resources == null || graph.Processes == null) return false;

            bool hasPageOrRidLock = graph.Resources.Any(r => r != null && r.LockType != null && (r.LockType.Contains("page") || r.LockType.Contains("rid")));

            bool hasWrites = graph.Processes.Any(p =>
            {
                if (p == null) return false;
                string sql = p.Inputbuf?.ToLowerInvariant() ?? "";
                return sql.Contains("insert") || sql.Contains("update") || sql.Contains("delete");
            });

            return hasPageOrRidLock && hasWrites;
        }

        private static bool HasForeignKeyPattern(DeadlockGraph graph)
        {
            if (graph == null || graph.Processes == null) return false;

            foreach (var proc in graph.Processes)
            {
                if (proc == null) continue;
                string sql = proc.Inputbuf?.ToLowerInvariant() ?? "";
                if (sql.Contains("foreign key") || sql.Contains("fk_") || sql.Contains("constraint") || sql.Contains("cascade"))
                    return true;
            }
            return false;
        }
    }
}




