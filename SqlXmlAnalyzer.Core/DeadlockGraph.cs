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
        string DeadlockPriority = "",
        string LogUsed = "0"
    )
    {
        public string RawXml { get; init; } = "";
        public IReadOnlyDictionary<string, string> RawFields { get; init; } = new Dictionary<string, string>();
        public Core.Services.SourceLocation Source { get; init; }
    }

    public sealed record ExecutionFrame(
        string Procname,
        string Line,
        string Statement
    )
    {
        public string RawXml { get; init; } = "";
        public IReadOnlyDictionary<string, string> RawFields { get; init; } = new Dictionary<string, string>();
        public Core.Services.SourceLocation Source { get; init; }
    }

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
        public string SourceId { get; init; } = "";
        public string ResourceKey { get; init; } = "";
        public IReadOnlyDictionary<string, string> RawFields { get; init; } = new Dictionary<string, string>();
        public string RawXml { get; init; } = "";
        public Core.Services.SourceLocation Source { get; init; }
        public string CleanTableName => string.IsNullOrEmpty(ObjectName) ? "(Unknown)" :
            (ObjectName.Contains(".") ? ObjectName.Split('.').Last() : ObjectName);

        public string OwnerModes => string.Join(", ", Owners.Select(o => o.Mode));
        public string WaiterModes => string.Join(", ", Waiters.Select(w => w.Mode));
        public string ConflictSummary => $"Own({OwnerModes}) ➔ Req({WaiterModes})";
    }

    public sealed record LockOwner(string Id, string Mode)
    {
        public IReadOnlyDictionary<string, string> RawFields { get; init; } = new Dictionary<string, string>();
        public Core.Services.SourceLocation Source { get; init; }
    }
    public sealed record LockWaiter(string Id, string Mode, string RequestType)
    {
        public IReadOnlyDictionary<string, string> RawFields { get; init; } = new Dictionary<string, string>();
        public Core.Services.SourceLocation Source { get; init; }
    }

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
        public IReadOnlySet<string> VictimProcessIds { get; internal set; } = new HashSet<string>();
        public List<DeadlockResourceLink> ResourceLinks { get; } = new();
        public DeadlockCycleAnalysis CycleAnalysis { get; internal set; } = DeadlockCycleAnalysis.Empty;
        public IReadOnlyList<string> Warnings { get; internal set; } = Array.Empty<string>();
        public string SourceFingerprint { get; internal set; } = "";
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
        public string EdgeId { get; set; } = "";
        public string WaiterLinkId { get; set; } = "";
        public string OwnerLinkId { get; set; } = "";
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
        public string CycleId { get; set; } = "";
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
    public static partial class DeadlockGraphBuilder
    {
        /// <summary>
        /// 从已解析的死锁数据构建完整的 Wait-For Graph
        /// </summary>
        // ==================== Mermaid 可视化生成 ====================

        /// <summary>
        /// 生成 Mermaid flowchart 代码（推荐直接复制到 https://mermaid.live 查看）
        /// </summary>
        public static string GenerateMermaid(DeadlockGraph graph, bool highlightCycle = true)
        {
            if (graph == null) return "flowchart TD\n    empty[\"无死锁图数据\"]";
            var sb = new StringBuilder("flowchart TD\n");
            string Node(string prefix, string id) => prefix + SanitizeId(id) + "_" + DeadlockIdentity.Hash(id)[..12];
            sb.AppendLine($"    summary[\"{EscapeMermaidLabel(graph.CycleAnalysis.Summary)}\"]");
            foreach (var process in graph.Processes)
            {
                bool victim = graph.VictimProcessIds.Contains(process.Id);
                string style = victim ? "victim" : highlightCycle && graph.CycleAnalysis.ProcessIds.Contains(process.Id) ? "cycle" : "normal";
                sb.AppendLine($"    {Node("p_", process.Id)}[\"{EscapeMermaidLabel(BuildProcessLabel(process, victim ? process.Id : null))}\"]:::{style}");
            }
            foreach (var resource in graph.Resources)
            {
                string label = resource.LockType + " " + resource.SourceId + " " + resource.ObjectName;
                if (!string.IsNullOrEmpty(resource.IndexName)) label += " (" + resource.IndexName + ")";
                sb.AppendLine($"    {Node("r_", resource.Id)}[\"{EscapeMermaidLabel(label)}\"]:::resource");
            }
            foreach (var link in graph.ResourceLinks)
            {
                string process = Node("p_", link.ProcessId), resource = Node("r_", link.ResourceId);
                string from = link.IsWaiter ? process : resource, to = link.IsWaiter ? resource : process;
                string arrow = highlightCycle && graph.CycleAnalysis.LinkIds.Contains(link.LinkId) ? "==>" : "-->";
                sb.AppendLine($"    {from} {arrow}|\"{EscapeMermaidLabel((link.IsWaiter ? "Req " : "Own ") + link.Mode + " " + link.RequestType)}\"| {to}");
            }
            sb.AppendLine("    classDef victim fill:#ffcccc,stroke:#cc0000,stroke-width:3px,color:#000000");
            sb.AppendLine("    classDef normal fill:#e6f3ff,stroke:#0066cc,color:#000000");
            sb.AppendLine("    classDef cycle fill:#fff2cc,stroke:#cc6600,stroke-width:3px,color:#000000");
            sb.AppendLine("    classDef resource fill:#fff2e6,stroke:#ff9933,color:#000000");
            return sb.ToString();
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
            sb.Append($"\\n优先级: {(string.IsNullOrEmpty(proc.DeadlockPriority) ? "N/A" : proc.DeadlockPriority)}");

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
            if (graph == null) return "无死锁图数据。";
            var sb = new StringBuilder(graph.CycleAnalysis.Summary);
            foreach (var cycle in graph.Cycles)
            {
                sb.AppendLine().AppendLine(cycle.GetCycleDescription());
                foreach (var edge in cycle.EdgesInCycle)
                    sb.AppendLine($"  {edge.FromProcessId} → {edge.ToProcessId}: {edge.Resource?.LockType} {edge.Resource?.SourceId} [{edge.RequestedMode} ← {edge.HeldMode}] edge={edge.EdgeId}");
            }
            return sb.ToString();
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
    )
    {
        public string RuleId { get; init; } = "";
        public string RuleVersion { get; init; } = "1.0.0";
        public string SourceFingerprint { get; init; } = "";
        public Core.Rules.DiagnosticConfidence Confidence { get; init; } = Core.Rules.DiagnosticConfidence.Unspecified;
        public IReadOnlyList<DeadlockEvidence> Evidence { get; init; } = Array.Empty<DeadlockEvidence>();
        public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
        public bool EvidenceTruncated { get; init; }
    }

    public static partial class DeadlockPatternAnalyzer
    {
        /// <summary>
        /// 基于 Wait-For Graph + 原始资源信息 + 锁模式，自动识别常见的死锁模式 ( 专家级诊断启发式 )
        /// </summary>
        private static bool HasConversionDeadlock(DeadlockGraph graph)
        {
            if (graph == null || graph.Edges == null) return false;
            foreach (var edge in graph.Edges)
            {
                if (edge == null) continue;
                string req = edge.RequestedMode?.ToUpper() ?? "";
                string held = edge.HeldMode?.ToUpper() ?? "";

                if ((held == "U" && req is "X" or "U") || (held == "S" && req == "X"))
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

        private static bool HasHighContentionOnSingleResource(DeadlockGraph graph)
        {
            if (graph == null || graph.Resources == null) return false;
            return graph.Resources.Any(r => r != null && r.Waiters != null && r.Waiters.Count >= 2);
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




