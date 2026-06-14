using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    public sealed class NodeDetail
    {
        public string NodeId { get; set; } = string.Empty;
        public string PhysicalOp { get; set; } = string.Empty;
        public double OwnCost { get; set; }
        public double SubtreeCost { get; set; }
    }

    public static class PlanDiagnosticAnalyzer
    {
        private const string R_IDX      = "1. 缺失索引建议与 DDL (Missing Indexes)";
        private const string R_CARD     = "2. 基数估计误差与根因 (Cardinality Error)";
        private const string R_CONV     = "3. 隐式转换风险 (Implicit Conv)";
        private const string R_TOP      = "4. 高开销硬件算子 Top 5 (High Cost)";
        private const string R_KEY      = "5. 键查找与回表开销 (Key Lookup)";
        private const string R_MEM      = "6. 内存预估与溢出落盘 (Memory Spills)";
        private const string R_SKEW     = "7. 并行数据倾斜瓶颈 (Thread Skew)";
        private const string R_RESID    = "8. 寻址残差谓词漏洞 (Residual Predicates)";
        private const string R_SNIFF    = "9. 参数嗅探反模式 (Parameter Sniffing)";
        private const string R_SCAN     = "10. 宽表全扫描风险 (Table Scan)";
        private const string R_UDF      = "11. 表变量与 TVF 黑洞 (UDF Bombs)";
        private const string R_WAIT     = "12. 引擎资源等待统计 (Wait Stats)";
        private const string R_ABORT    = "13. 优化器提前中止 (Optimizer Abort)";
        private const string R_PATTERN  = "14. 🧩 经典 SQL 反模式深潜 (Pattern Recognition)";
        private const string R_REWRITE  = "15. 💡 T-SQL 智能改写多维代码块处方 (Query Rewrite Blocks)";
        private const string R_SEMAPHORE = "16. 🚦 内存资源准入等待 (Resource Semaphore)";
        private const string R_CACHE     = "17. ♻️ 缓存命中与重编译开销 (Cache Hit & Recompile)";

        public static string GenerateDiagnosticReport(XDocument doc, XNamespace ns)
        {
            if (doc?.Root == null) return "⚠️ 无效的执行计划 XML 结构。";

            Logger.Info($"GenerateDiagnosticReport: 开始执行计划深度诊断 | Root={doc.Root.Name}");

            try
            {
                var reports = new Dictionary<string, List<string>>
                {
                    { R_IDX, new List<string>() },
                    { R_CARD, new List<string>() },
                    { R_CONV, new List<string>() },
                    { R_TOP, new List<string>() },
                    { R_KEY, new List<string>() },
                    { R_MEM, new List<string>() },
                    { R_SKEW, new List<string>() },
                    { R_RESID, new List<string>() },
                    { R_SNIFF, new List<string>() },
                    { R_SCAN, new List<string>() },
                    { R_UDF, new List<string>() },
                    { R_WAIT, new List<string>() },
                    { R_ABORT, new List<string>() },
                    { R_PATTERN, new List<string>() },
                    { R_REWRITE, new List<string>() },
                    { R_SEMAPHORE, new List<string>() },
                    { R_CACHE, new List<string>() }
                };

                // 1. Wait Stats
                try
                {
                    var waitStats = doc.Descendants(ns + "WaitStats").Descendants(ns + "Wait");
                    foreach (var ws in waitStats)
                    {
                        if (ws == null) continue;
                        string wtype = ws.Attribute("WaitType")?.Value ?? "";
                        double wtime = ParseDouble(ws.Attribute("WaitTimeMs")?.Value);
                        if (wtype.Contains("RESOURCE_SEMAPHORE"))
                        {
                            reports[R_SEMAPHORE].Add($"🚦 内存准入排队 [{wtype}]: 耗时 {wtime:F0} 毫秒。这表示服务器并发查询消耗了大量内存，当前查询被迫排队等待内存分配 (Memory Grant Waiting)。严重影响整体吞吐率！");
                        }
                        else if (wtime > 100)
                        {
                            reports[R_WAIT].Add($"⏱️ 发现显著资源等待 [{wtype}]: 累积耗时高达 {wtime:F0} 毫秒。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"WaitStats 诊断异常: {ex.Message}");
                }

                // 2. Optimizer Abort
                try
                {
                    var stmts = doc.Descendants(ns + "StmtSimple");
                    foreach (var stmt in stmts)
                    {
                        if (stmt == null) continue;
                        string? abortReason = stmt.Attribute("StatementOptmEarlyAbortReason")?.Value;
                        if (!string.IsNullOrEmpty(abortReason) && abortReason != "GoodEnoughPlanFound")
                        {
                            reports[R_ABORT].Add($"🚨 SQL 优化器因 [{abortReason}] 提前中止，未能生成最优计划。这代表计划极其复杂或相关统计信息缺失。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Optimizer Abort 诊断异常: {ex.Message}");
                }

                // Cache & Recompile
                try
                {
                    var queryTimeStats = doc.Descendants(ns + "QueryTimeStats").FirstOrDefault();
                    if (queryTimeStats != null)
                    {
                        double compileTime = ParseDouble(queryTimeStats.Attribute("CompileTime")?.Value);
                        double compileCPU = ParseDouble(queryTimeStats.Attribute("CompileCPU")?.Value);
                        if (compileTime > 500)
                        {
                            reports[R_CACHE].Add($"♻️ 重编译高开销: 编译时间 {compileTime:F0} 毫秒 (CPU: {compileCPU:F0} 毫秒)。这表明查询未能命中计划缓存 (Cache Miss) 或发生了重编译 (Recompile)，建议检查统计信息更新频率或使用参数化查询。");
                        }
                    }
                    var stmtSimple = doc.Descendants(ns + "StmtSimple").FirstOrDefault();
                    if (stmtSimple != null)
                    {
                        string reason = stmtSimple.Attribute("StatementOptmLevel")?.Value ?? "";
                        if (reason == "FULL")
                        {
                            double cost = ParseDouble(stmtSimple.Attribute("StatementSubTreeCost")?.Value);
                            if (cost > 50)
                            {
                                reports[R_CACHE].Add($"⚠️ 复杂计划编译: 优化器进行了 FULL 级别的深度编译，计划预估开销高达 {cost:F1}。如果此查询高频执行，CPU 会被彻底耗尽。");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Cache & Recompile 诊断异常: {ex.Message}");
                }

                // 3. Missing Indexes
                try
                {
                    var missingIndexes = ExtractMissingIndexes(doc, ns);
                    foreach (var mi in missingIndexes)
                    {
                        string dbaTip = mi.IncludeColumns.Count > 0 ? "\n   [DBA 提示] 包含 (INCLUDE) 列的总长度在某些 SQL Server 版本中受 1023 字节或 32 个列的限制，请视情况裁剪。" : "";
                        reports[R_IDX].Add($"⭐ 评分: {mi.Score}/100 | 预估提升: {mi.Impact:F1}% | 推荐覆盖索引建表 DDL:\n   👉 {mi.CreateIndexStatement}{dbaTip}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Missing Indexes 诊断异常: {ex.Message}");
                }

                // 4. Implicit Conversion
                try
                {
                    var convs = new HashSet<string>();
                    var scalarOps = doc.Descendants(ns + "ScalarOperator");
                    foreach (var op in scalarOps)
                    {
                        if (op == null) continue;
                        string s = op.Attribute("ScalarString")?.Value ?? "";
                        if (s.Contains("CONVERT_IMPLICIT"))
                        {
                            convs.Add(s);
                        }
                    }
                    var pacs = doc.Descendants(ns + "PlanAffectingConvert");
                    foreach (var pac in pacs)
                    {
                        if (pac == null) continue;
                        string expr = pac.Attribute("Expression")?.Value ?? "";
                        if (expr.Contains("CONVERT_IMPLICIT"))
                        {
                            convs.Add(expr);
                        }
                    }
                    foreach (var c in convs.Distinct())
                    {
                        reports[R_CONV].Add($"⚠️ 隐式转换风险: SQL 引擎执行了 CONVERT_IMPLICIT。这通常由于字段类型不匹配引起，极易导致索引扫描失效（Index Scan）：\n   👉 表达式: {c}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Implicit Conversion 诊断异常: {ex.Message}");
                }

                // 5. Parameter Sniffing & Statistics Usage
                try
                {
                    var paramCols = doc.Descendants(ns + "ParameterList").Descendants(ns + "ColumnReference");
                    foreach (var p in paramCols)
                    {
                        if (p == null) continue;
                        string col = p.Attribute("Column")?.Value ?? "";
                        string? comp = p.Attribute("ParameterCompiledValue")?.Value;
                        string? run = p.Attribute("ParameterRuntimeValue")?.Value;
                        if (!string.IsNullOrEmpty(comp) && !string.IsNullOrEmpty(run) && comp != run)
                        {
                            reports[R_SNIFF].Add($"🧵 参数嗅探警告 on {col}:\n   • 首次编译缓存值 (Compiled): [{comp}]\n   • 运行时传入值 (Runtime): [{run}]\n   👉 [专家处方]: 首次编译值和实际运行时参数不同，当两值数据分布差异极大时极易引发“嗅探灾难”（选用次优查询方案导致运行缓慢）。建议对该 SQL 语句末尾附加 `OPTION (RECOMPILE)` 提示。");
                        }
                    }

                    // OptimizerStatsUsage 诊断
                    var statsList = SqlXmlAnalyzer.Core.Parsers.StatisticsUsageParser.Parse(doc, ns);
                    if (statsList.Count > 0)
                    {
                        var sbStats = new StringBuilder();
                        sbStats.AppendLine("📊 优化器统计信息使用状态 (OptimizerStatsUsage):");
                        foreach (var stat in statsList)
                        {
                            string warningDetails = "";
                            if (stat.IsStale)
                            {
                                warningDetails += $" ⚠️ 已过时 (更新账龄: {stat.AgeInDays}天)";
                            }
                            if (stat.ModificationCount > 1000)
                            {
                                warningDetails += $" ⚠️ 频繁变动 (修改次数: {stat.ModificationCount:N0})";
                            }
                            if (stat.IsLowSampling)
                            {
                                warningDetails += $" ⚠️ 低采样率 (采样率: {stat.SamplingPercent:F1}%)";
                            }

                            string statusIcon = string.IsNullOrEmpty(warningDetails) ? "✅" : "⚠️";
                            sbStats.AppendLine($"   • {statusIcon} [{stat.Database}].[{stat.Schema}].[{stat.Table}] (统计项: {stat.Statistics}){warningDetails}");

                            if (!string.IsNullOrEmpty(warningDetails))
                            {
                                sbStats.AppendLine($"     👉 优化建议: UPDATE STATISTICS [{stat.Database}].[{stat.Schema}].[{stat.Table}]({stat.Statistics}) WITH FULLSCAN;");
                            }
                        }
                        reports[R_SNIFF].Add(sbStats.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Parameter Sniffing / Statistics 诊断异常: {ex.Message}");
                }

                // 6. Memory Grant 过度预估警告
                try
                {
                    var memGrant = doc.Descendants(ns + "MemoryGrantInfo").FirstOrDefault();
                    if (memGrant != null)
                    {
                        double granted = ParseDouble(memGrant.Attribute("GrantedMemory")?.Value);
                        double used = ParseDouble(memGrant.Attribute("MaxUsedMemory")?.Value);
                        if (granted > 10240 && used > 0 && (used / granted) < 0.1)
                        {
                            reports[R_MEM].Add($"💾 [资源空置浪费]: 内存预估过度！本查询总共申请排队并占用了 {granted / 1024.0:F1} MB 内存，但实际运行中仅最大消耗了 {used / 1024.0:F1} MB (内存利用率低于 10%)。并发极易导致 RESOURCE_SEMAPHORE 锁等待，建议更新涉及表的统计信息。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Memory Grant 诊断异常: {ex.Message}");
                }

                // 7. RelOp-level diagnostics
                var relOps = new List<XElement>();
                try
                {
                    relOps = doc.Descendants(ns + "RelOp").ToList();

                    // ==========================================
                    // 🔌 扩展架构：执行 30 条核心诊断规则 (RuleEngine)
                    // ==========================================
                    var ruleEngine = new SqlXmlAnalyzer.Core.Rules.RuleEngine();
                    ruleEngine.RegisterDefaultRules(); // Registers the 6 implemented P0 rules

                    foreach (var relOp in relOps)
                    {
                        var ruleResults = ruleEngine.AnalyzeNode(relOp, ns);
                        foreach (var result in ruleResults)
                        {
                            string prefix = result.Severity == "Critical" ? "❌ 严重:" : "⚠️ 警告:";
                            string msg = $"{prefix} [Node {result.NodeId}] {result.Title}\n{result.Message}";
                            
                            // Map RuleId to existing categories (or put in Pattern category)
                            if (result.RuleId.Contains("CONVERSION")) reports[R_CONV].Add(msg);
                            else if (result.RuleId.Contains("KEY_LOOKUP")) reports[R_KEY].Add(msg);
                            else if (result.RuleId.Contains("PARAM_SNIFFING")) reports[R_SNIFF].Add(msg);
                            else if (result.RuleId.Contains("ESTIMATE_MISMATCH")) reports[R_CARD].Add(msg);
                            else if (result.RuleId.Contains("MEMORY_GRANT") || result.RuleId.Contains("SPILL")) reports[R_MEM].Add(msg);
                            else if (result.RuleId.Contains("PARALLEL") || result.RuleId.Contains("SKEW")) reports[R_SKEW].Add(msg);
                            else if (result.RuleId.Contains("RESIDUAL_PRED") || result.RuleId.Contains("NON_SARGABLE")) reports[R_RESID].Add(msg);
                            else if (result.RuleId.Contains("UDF_TVF")) reports[R_UDF].Add(msg);
                            else if (result.RuleId.Contains("NESTED_LOOPS_HIGH_EXEC")) reports[R_PATTERN].Add(msg);
                            else reports[R_PATTERN].Add(msg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("无法获取 RelOp 节点列表", ex);
                }

                var allNodeDetails = new List<NodeDetail>();

                foreach (var relOp in relOps)
                {
                    if (relOp == null) continue;
                    string nodeId = relOp.Attribute("NodeId")?.Value ?? "?";

                    try
                    {
                        string physOp = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";
                        string logical = relOp.Attribute("LogicalOp")?.Value ?? "";
                        double estRows = ParseDouble(relOp.Attribute("EstimateRows")?.Value);
                        double subtreeCost = ParseDouble(relOp.Attribute("EstimatedTotalSubtreeCost")?.Value);
                        
                        // 计算 own_cost
                        double ownCost = 0.0;
                        try
                        {
                            var childRelOps = GetDirectChildRelOps(relOp, ns);
                            double childrenCost = childRelOps.Select(c => ParseDouble(c?.Attribute("EstimatedTotalSubtreeCost")?.Value)).Sum();
                            ownCost = Math.Max(0.0, subtreeCost - childrenCost);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning($"NodeId {nodeId} 计算 OwnCost 异常: {ex.Message}");
                        }

                        allNodeDetails.Add(new NodeDetail
                        {
                            NodeId = nodeId,
                            PhysicalOp = physOp,
                            OwnCost = ownCost,
                            SubtreeCost = subtreeCost
                        });

                        // Runtime info
                        double actRows = 0;
                        double actExecs = 0;
                        bool hasActual = false;
                        var threadRows = new Dictionary<string, double>();
                        
                        var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                        if (runTimeInfo != null)
                        {
                            hasActual = true;
                            var counters = runTimeInfo.Descendants(ns + "RunTimeCountersPerThread");
                            foreach (var rc in counters)
                            {
                                if (rc == null) continue;
                                string tid = rc.Attribute("Thread")?.Value ?? "0";
                                double r = ParseDouble(rc.Attribute("ActualRows")?.Value);
                                double e = ParseDouble(rc.Attribute("ActualExecutions")?.Value);
                                if (e < 1) e = 1;
                                
                                threadRows[tid] = r;
                                actRows += r;
                                actExecs += e;
                            }
                        }
                        if (actExecs < 1) actExecs = 1;

                        // 基数估计误差 (Cardinality Error)
                        if (hasActual)
                        {
                            double avgActRows = actRows / actExecs;
                            if (avgActRows > 100 || estRows > 100)
                            {
                                double diff = Math.Abs(estRows - avgActRows);
                                double ratio = Math.Max(estRows, avgActRows) / Math.Max(Math.Min(estRows, avgActRows), 1.0);
                                if (ratio > 10 && diff > 1000)
                                {
                                    string reason = "";
                                    string pred = ExtractPredicates(relOp, ns);
                                    if (pred.Contains("AND"))
                                    {
                                        reason = " 🎯 根因: 存在多列联合过滤(AND)，多统计信息缺失或优化器低估。建议针对多列创建联合统计信息。";
                                    }
                                    else if (HasFunctionWrapper(pred))
                                    {
                                        reason = " 🎯 根因: 过滤列被标量函数包裹，导致统计信息失效。建议剥离标量函数。";
                                    }
                                    reports[R_CARD].Add($"🚨 基数估计偏离 Node {nodeId} ({physOp}): 预估单次行数 {estRows:F0}，实际单次 {avgActRows:F0} (偏差达 {ratio:F1} 倍)。{reason}");
                                }
                            }
                        }

                        // 键查找 (Key Lookup)
                        if (physOp == "Key Lookup" || physOp == "RID Lookup")
                        {
                            string objName = ExtractObjectName(relOp, ns);
                            // 增加 DBA 建议：避免 SELECT * 可以彻底消除回表
                            reports[R_KEY].Add($"🔖 Lookup 回表 Node {nodeId}: 对表 {objName} 发生了回表查找（Key Lookup）。说明所检索非聚集索引没有完全覆盖字段。推荐使用覆盖索引。 [DBA 提示] 请同时检查 SQL 语句是否滥用了 SELECT *，若能移除不必要输出的列，可直接免去回表开销。");
                        }

                        // 溢出警告 (Memory Spills)
                        var warningsEl = relOp.Element(ns + "Warnings");
                        if (warningsEl != null)
                        {
                            var warnList = warningsEl.Elements().Where(e => e != null).Select(e => e.Name.LocalName).ToList();
                            if (warnList.Count > 0)
                            {
                                reports[R_MEM].Add($"⚠️ 算子告警 Node {nodeId} ({physOp}): 执行引擎爆发了 [ {string.Join(", ", warnList)} ] 警告！发生了排序或哈希的溢出并被迫落盘 TempDB！IO 性能遭受了毁灭性打击！");
                            }
                        }

                        // 数据倾斜 (Thread Skew)
                        if (threadRows.Count > 1)
                        {
                            var workerRows = threadRows.Where(kv => kv.Key != "0").Select(kv => kv.Value).ToList();
                            if (workerRows.Count > 1 && workerRows.Sum() > 1000)
                            {
                                double maxR = workerRows.Max();
                                double avgR = workerRows.Average();
                                if (maxR > avgR * 2.0 && maxR > 100)
                                {
                                    reports[R_SKEW].Add($"⚡ 线程倾斜 Node {nodeId} ({physOp}): 并行数据倾斜！最大线程分配了 {maxR:F0} 行 (平均行数仅 {avgR:F0})。拖慢了整体吞吐速度。");
                                }
                            }
                        }

                        // 宽表全盲扫描 (Table Scan)
                        if (physOp == "Table Scan" || physOp == "Clustered Index Scan")
                        {
                            string seekPred = ExtractSeekPredicate(relOp, ns);
                            string pred = ExtractPredicates(relOp, ns);
                            double tableCard = ParseDouble(relOp.Attribute("TableCardinality")?.Value);
                            if (tableCard < 1) tableCard = 1;
                            
                            if (string.IsNullOrEmpty(seekPred) && string.IsNullOrEmpty(pred) && estRows > 1000 && (estRows / tableCard) > 0.8)
                            {
                                string objName = ExtractObjectName(relOp, ns);
                                reports[R_SCAN].Add($"🚫 宽表扫描 Node {nodeId}: 在无任何 Where 过滤谓词（无Seek/Scan过滤条件）的情况下，对大表 {objName} 扫描了超过 80% 记录 (扫描行数 {estRows:F0} / {tableCard:F0})。建议做 limit 限制或添加定位过滤条件。");
                            }
                        }

                        // 残差谓词 (Residual Predicate)
                        if (physOp.Contains("Seek") && !string.IsNullOrEmpty(ExtractSeekPredicate(relOp, ns)))
                        {
                            var residualPred = ExtractResidualPredicate(relOp, ns);
                            if (!string.IsNullOrEmpty(residualPred))
                            {
                                bool isResidualWarning = false;
                                double actRowsRead = 0;
                                var runtime = relOp.Element(ns + "RunTimeInformation");
                                if (runtime != null)
                                {
                                    var counters = runtime.Descendants(ns + "RunTimeCountersPerThread");
                                    foreach (var rc in counters)
                                    {
                                        if (rc == null) continue;
                                        actRowsRead += ParseDouble(rc.Attribute("ActualRowsRead")?.Value);
                                    }
                                }
                                
                                if (actRows > 0 && actRowsRead > actRows * 1.2 && (actRowsRead - actRows) > 100)
                                {
                                    isResidualWarning = true;
                                }
                                else if (runtime == null)
                                {
                                    isResidualWarning = true;
                                }
                                
                                if (isResidualWarning)
                                {
                                    string objName = ExtractObjectName(relOp, ns);
                                    // 增加 DBA 提示：Bitmap Filter 可能会导致 ActualRowsRead 数据不准确
                                    reports[R_RESID].Add($"🚨 残差谓词 Node {nodeId} ({physOp} on {objName}): 定位虽走 Seek 索引，但在过滤列上只有前导列被用于 Seek，其他过滤列作为了“残差谓词”在内存中强行二次比对过滤，带来多余 IO 开销。建议把 [{residualPred}] 包含的列加入复合索引。 [DBA 提示] 若当前为并行查询且启用了 Bitmap Filter，ActualRowsRead 可能偏高，需结合实际执行时间综合判断。");
                                }
                            }
                        }


                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"诊断算子 Node {nodeId} 时发生异常: {ex.Message}");
                    }
                }

                // 8. 独占硬件开销 TOP 5
                try
                {
                    var topNodes = allNodeDetails.OrderByDescending(n => n.OwnCost).Take(5).ToList();
                    foreach (var node in topNodes)
                    {
                        if (node == null) continue;
                        if (node.OwnCost > 0.005)
                        {
                            reports[R_TOP].Add($"⏱️ 算子 Node {node.NodeId} ({node.PhysicalOp}): 独占单体硬件开销预估高达 {node.OwnCost:F4} (占该算子子树开销的 {(node.OwnCost / Math.Max(node.SubtreeCost, 0.001)) * 100.0:F1}%)。建议在此算子做重点定位。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"独占硬件开销诊断异常: {ex.Message}");
                }

                // ================= 经典模式识别 =================
                try
                {
                    var nestedLoops = relOps.Where(r => r != null && r.Attribute("PhysicalOp")?.Value == "Nested Loops").ToList();
                    if (nestedLoops.Count >= 2)
                    {
                        foreach (var nl in nestedLoops)
                        {
                            if (nl == null) continue;
                            var childOps = GetDirectChildRelOps(nl, ns);
                            var innerSeek = childOps.FirstOrDefault(c => c != null && c.Attribute("PhysicalOp")?.Value.Contains("Seek") == true);
                            if (innerSeek != null)
                            {
                                string sp = ExtractSeekPredicate(innerSeek, ns);
                                bool hasInequality = sp.Contains("<") || sp.Contains(">");
                                if (hasInequality)
                                {
                                    string nlNodeId = nl.Attribute("NodeId")?.Value ?? "?";
                                    reports[R_PATTERN].Add($"🚨 **[隐蔽风暴] 检测到嵌套循环 + 不等式查找 模式 (在 Node {nlNodeId})**\n   这代表 SQL Server 在处理一个典型的“累积和(Running Total)”或“时间区间重叠”反模式，可能引发 CPU O(N²) 暴增。\n   👉 **优化处方**: 强烈建议重写为 Window 窗口函数 (SUM() OVER (ORDER BY...)) 或修正不等式关联。");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"经典模式识别 - 嵌套循环 诊断异常: {ex.Message}");
                }

                // Multiple scalar subqueries
                try
                {
                    var stmts = doc.Descendants(ns + "StmtSimple");
                    foreach (var stmt in stmts)
                    {
                        if (stmt == null) continue;
                        string sqlText = stmt.Attribute("StatementText")?.Value ?? "";
                        if (!string.IsNullOrEmpty(sqlText))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(sqlText, @"\bFROM\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                string selectClause = sqlText.Substring(0, match.Index);
                                int scalarCount = System.Text.RegularExpressions.Regex.Matches(selectClause, @"\(\s*SELECT\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
                                if (scalarCount >= 2)
                                {
                                    reports[R_PATTERN].Add($"🚨 **[设计缺陷] SELECT 列表中检测到 {scalarCount} 个标量子查询！**\n   每个子查询等同于每一行触发一次单独的隐式游标调用，造成性能灾难。\n   👉 **重构建议: 强制整合为一个 (LEFT JOIN) 或使用 CROSS APPLY 统一计算。**");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"经典模式识别 - 标量子查询 诊断异常: {ex.Message}");
                }

                // ================= T-SQL Rewrite =================
                try
                {
                    if (reports[R_CONV].Count > 0) reports[R_REWRITE].Add("💡 [隐式转换修复]\n-- ❌ 原写法 (导致全表扫描):\nSELECT * FROM Table WHERE VarcharCol = N'123' \n-- ✅ 优化 (恢复 Index Seek):\nSELECT * FROM Table WHERE VarcharCol = CAST('123' AS VARCHAR(100))");
                    if (reports[R_UDF].Count > 0) reports[R_REWRITE].Add("💡 [表变量性能黑洞修复]\n-- ❌ 原写法 (缺乏统计信息):\nDECLARE @Tmp TABLE (Id INT);\nINSERT INTO @Tmp...\n\n-- ✅ 优化 (临时表有独立直方图支持分布):\nCREATE TABLE #Tmp (Id INT);\nINSERT INTO #Tmp...\n-- (记得 DROP TABLE #Tmp)");
                    if (reports[R_SNIFF].Count > 0) reports[R_REWRITE].Add("💡 [参数嗅探 4 种解法]\n👉 解法 A (表小/查询快): 在末尾加 `OPTION (RECOMPILE)`\n👉 解法 B (查询极复杂): 在末尾加 `OPTION (OPTIMIZE FOR UNKNOWN)`\n👉 解法 C (查询极偏斜): 在末尾加 `OPTION (OPTIMIZE FOR (@p = '典型值'))`\n👉 解法 D (存储过程局部变量骗过优化器): \n   DECLARE @L_Var INT = @Param;\n   SELECT ... WHERE Col = @L_Var;");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Rewrite 建议生成异常: {ex.Message}");
                }

                // 汇总输出 Markdown 格式
                var sb = new StringBuilder();
                sb.AppendLine("========================================================================");
                sb.AppendLine("★ SQL Server 专家级执行计划深度诊断报告（Plan Explorer 推荐）★");
                sb.AppendLine("========================================================================");
                sb.AppendLine();

                int totalIssues = 0;
                foreach (var kv in reports)
                {
                    if (kv.Value.Count > 0)
                    {
                        totalIssues += kv.Value.Count;
                        sb.AppendLine($"【{kv.Key}】");
                        sb.AppendLine("------------------------------------------------------------------------");
                        foreach (var issue in kv.Value)
                        {
                            sb.AppendLine(issue);
                            sb.AppendLine();
                        }
                    }
                }

                if (totalIssues == 0)
                {
                    sb.AppendLine("💚 恭喜！当前执行计划在 17 项核心健康度诊断中完美通过，未检测到任何反模式或硬伤隐患。");
                }
                else
                {
                    sb.Insert(0, $"💡 针对当前计划共扫描出 {totalIssues} 个核心性能隐患/优化点。请查看以下各项深度建议，对症下药：\n\n");
                }

                Logger.Info($"GenerateDiagnosticReport: 诊断完成 | 共发现 {totalIssues} 个隐患/优化点");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogException("PlanDiagnosticAnalyzer.GenerateDiagnosticReport", ex);
                return $"⚠️ 执行计划分析诊断过程中发生错误: {ex.Message}";
            }
        }

        public static List<XElement> GetDirectChildRelOps(XElement element, XNamespace ns)
        {
            var children = new List<XElement>();
            if (element == null) return children;

            try
            {
                var stack = new Stack<XElement>();
                
                var childList = element.Elements().ToList();
                for (int i = childList.Count - 1; i >= 0; i--)
                {
                    var ch = childList[i];
                    if (ch != null) stack.Push(ch);
                }
                
                while (stack.Count > 0)
                {
                    var child = stack.Pop();
                    if (child == null) continue;

                    if (child.Name == ns + "RelOp")
                    {
                        children.Add(child);
                    }
                    else
                    {
                        var innerList = child.Elements().ToList();
                        for (int i = innerList.Count - 1; i >= 0; i--)
                        {
                            var ich = innerList[i];
                            if (ich != null) stack.Push(ich);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"GetDirectChildRelOps 遍历异常: {ex.Message}");
            }
            return children;
        }

        private static double ParseDouble(string? val)
        {
            if (string.IsNullOrEmpty(val)) return 0.0;
            if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double res)) return res;
            return 0.0;
        }

        public static List<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion> ExtractMissingIndexes(XDocument doc, XNamespace ns)
        {
            var results = new List<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion>();
            var missingIndexGroups = doc.Descendants(ns + "MissingIndexGroup");
            foreach (var mig in missingIndexGroups)
            {
                if (mig == null) continue;
                double impact = ParseDouble(mig.Attribute("Impact")?.Value);
                var mis = mig.Descendants(ns + "MissingIndex");
                foreach (var mi in mis)
                {
                    if (mi == null) continue;
                    var suggestion = new SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion
                    {
                        Schema = mi.Attribute("Schema")?.Value ?? "",
                        Table = mi.Attribute("Table")?.Value ?? "",
                        Impact = impact
                    };
                    
                    foreach (var cg in mi.Descendants(ns + "ColumnGroup"))
                    {
                        if (cg == null) continue;
                        string usage = cg.Attribute("Usage")?.Value ?? "";
                        var cols = cg.Descendants(ns + "Column")
                            .Select(c => c.Attribute("Name")?.Value ?? "")
                            .Where(n => n != "")
                            .Select(n => new SqlXmlAnalyzer.Core.Models.IndexColumn { Name = n, Usage = usage })
                            .ToList();
                        
                        if (usage == "EQUALITY" || usage == "INEQUALITY")
                        {
                            suggestion.KeyColumns.AddRange(cols);
                        }
                        else if (usage == "INCLUDE")
                        {
                            suggestion.IncludeColumns.AddRange(cols);
                        }
                    }
                    
                    if (suggestion.KeyColumns.Count > 0)
                    {
                        SqlXmlAnalyzer.Core.Scoring.IndexScoringCalculator.CalculateScore(suggestion, doc, ns);
                        results.Add(suggestion);
                    }
                }
            }
            return results;
        }

        private static string ExtractObjectName(XElement relOp, XNamespace ns)
        {
            if (relOp == null) return "(未知表)";
            try
            {
                var objEl = relOp.Descendants(ns + "Object").FirstOrDefault();
                if (objEl != null)
                {
                    string table = objEl.Attribute("Table")?.Value?.Trim('[', ']') ?? "";
                    string index = objEl.Attribute("Index")?.Value?.Trim('[', ']') ?? "";
                    return string.IsNullOrEmpty(index) ? $"[{table}]" : $"[{table}].[{index}]";
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ExtractObjectName 提取异常: {ex.Message}");
            }
            return "(未知表)";
        }

        private static string ExtractPredicates(XElement relOp, XNamespace ns)
        {
            if (relOp == null) return "";
            var preds = new List<string>();
            try
            {
                foreach (var elem in relOp.Elements())
                {
                    if (elem == null) continue;
                    if (elem.Name.LocalName != "OutputList" && 
                        elem.Name.LocalName != "Warnings" && 
                        elem.Name.LocalName != "RunTimeInformation" && 
                        elem.Name.LocalName != "RelOp")
                    {
                        var scalarOps = elem.Descendants(ns + "ScalarOperator");
                        foreach (var op in scalarOps)
                        {
                            if (op == null) continue;
                            string? s = op.Attribute("ScalarString")?.Value;
                            if (!string.IsNullOrEmpty(s) && !preds.Contains(s))
                            {
                                preds.Add(s);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ExtractPredicates 提取异常: {ex.Message}");
            }
            return string.Join(" AND ", preds);
        }

        private static bool HasFunctionWrapper(string pred)
        {
            if (string.IsNullOrEmpty(pred)) return false;
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(pred, @"\w+\s*\(.*?\[.+?\]");
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractSeekPredicate(XElement relOp, XNamespace ns)
        {
            if (relOp == null) return "";
            var preds = new List<string>();
            try
            {
                var seekPreds = relOp.Descendants(ns + "SeekPredicates").Descendants(ns + "ScalarOperator")
                    .Concat(relOp.Descendants(ns + "SeekPredicateNew").Descendants(ns + "ScalarOperator"));
                foreach (var op in seekPreds)
                {
                    if (op == null) continue;
                    string? s = op.Attribute("ScalarString")?.Value;
                    if (!string.IsNullOrEmpty(s) && !preds.Contains(s))
                    {
                        preds.Add(s);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ExtractSeekPredicate 提取异常: {ex.Message}");
            }
            return string.Join(" AND ", preds);
        }

        private static string ExtractResidualPredicate(XElement relOp, XNamespace ns)
        {
            if (relOp == null) return "";
            try
            {
                var predEl = relOp.Element(ns + "Predicate");
                if (predEl != null)
                {
                    return predEl.Descendants(ns + "ScalarOperator").FirstOrDefault()?.Attribute("ScalarString")?.Value ?? "";
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ExtractResidualPredicate 提取异常: {ex.Message}");
            }
            return "";
        }
    }
}

