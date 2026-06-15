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

        public static List<SqlXmlAnalyzer.Core.Rules.AnalysisResult> AnalyzePlan(XDocument doc, XNamespace ns, string configPath = "RuleConfiguration.json")
        {
            var results = new List<SqlXmlAnalyzer.Core.Rules.AnalysisResult>();
            if (doc?.Root == null) return results;

            var ruleEngine = new SqlXmlAnalyzer.Core.Rules.RuleEngine(configPath);
            ruleEngine.RegisterDefaultRules();

            var relOps = doc.Descendants(ns + "RelOp").ToList();
            XElement? dummyRelOp = null;
            bool hasNodeZero = relOps.Any(r => r.Attribute("NodeId")?.Value == "0");
            if (!hasNodeZero && doc.Root != null)
            {
                dummyRelOp = new XElement(ns + "RelOp", new XAttribute("NodeId", "0"));
                doc.Root.Add(dummyRelOp);
                relOps.Add(dummyRelOp);
            }

            try
            {
                foreach (var relOp in relOps)
                {
                    var ruleResults = ruleEngine.AnalyzeNode(relOp, ns);
                    results.AddRange(ruleResults);
                }
            }
            finally
            {
                if (dummyRelOp != null)
                {
                    dummyRelOp.Remove();
                }
            }

            return results;
        }

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

                var ruleResults = AnalyzePlan(doc, ns);

                foreach (var result in ruleResults)
                {
                    string category = MapRuleIdToCategory(result.RuleId);
                    if (IsNodeLevelRule(result.RuleId))
                    {
                        string prefix = result.Severity == "Critical" ? "❌ 严重:" : "⚠️ 警告:";
                        string msg = $"{prefix} [Node {result.NodeId}] {result.Title}\n{result.Message}";
                        reports[category].Add(msg);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(result.Message))
                        {
                            var parts = result.Message.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var part in parts)
                            {
                                reports[category].Add(part);
                            }
                        }
                    }
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

        private static string MapRuleIdToCategory(string ruleId)
        {
            switch (ruleId)
            {
                case "RULE_020_MISSING_INDEX":
                case "RULE_035_SARGABLE_INDEX_RECOMMENDATION":
                    return R_IDX;
                case "RULE_004_ESTIMATE_MISMATCH":
                case "RULE_016_ZERO_ROW_ACTUALS":
                case "RULE_030_CARDINALITY_ERROR":
                    return R_CARD;
                case "RULE_001_IMPLICIT_CONV":
                case "RULE_026_IMPLICIT_CONV_DOC":
                    return R_CONV;
                case "RULE_022_HIGH_COST_OP":
                    return R_TOP;
                case "RULE_002_KEY_LOOKUP":
                case "RULE_031_KEY_LOOKUP_OP":
                    return R_KEY;
                case "RULE_017_LARGE_MEMORY_GRANT":
                case "RULE_008_SPILL_DETECTION":
                case "RULE_029_MEMORY_GRANT_DOC":
                case "RULE_032_MEMORY_SPILL":
                    return R_MEM;
                case "RULE_009_PARALLEL_SKEW":
                case "RULE_033_THREAD_SKEW":
                    return R_SKEW;
                case "RULE_006_RESIDUAL_PREDICATE":
                case "RULE_007_NON_SARGABLE":
                case "RULE_034_RESIDUAL_PRED_OP":
                    return R_RESID;
                case "RULE_003_PARAM_SNIFFING":
                case "RULE_027_PARAM_SNIFFING_DOC":
                case "RULE_028_STATS_USAGE":
                    return R_SNIFF;
                case "RULE_021_TABLE_SCAN":
                    return R_SCAN;
                case "RULE_011_UDF_TVF":
                    return R_UDF;
                case "RULE_016_WAIT_STATS":
                    return R_WAIT;
                case "RULE_018_OPTIMIZER_ABORT":
                    return R_ABORT;
                case "RULE_012_NESTED_LOOPS_HIGH_EXEC":
                case "RULE_013_ANTI_PATTERN":
                case "RULE_014_SERIAL_PLAN_REASON":
                case "RULE_015_LOCAL_VARIABLES":
                case "RULE_023_RUNNING_TOTAL_PATTERN":
                case "RULE_024_SCALAR_SUBQUERY_PATTERN":
                    return R_PATTERN;
                case "RULE_025_QUERY_REWRITE":
                    return R_REWRITE;
                case "RULE_017_RESOURCE_SEMAPHORE":
                    return R_SEMAPHORE;
                case "RULE_019_CACHE_RECOMPILE":
                    return R_CACHE;
                default:
                    if (ruleId.Contains("CONV")) return R_CONV;
                    if (ruleId.Contains("KEY_LOOKUP")) return R_KEY;
                    if (ruleId.Contains("PARAM_SNIFFING")) return R_SNIFF;
                    if (ruleId.Contains("ESTIMATE_MISMATCH") || ruleId.Contains("CARDINALITY")) return R_CARD;
                    if (ruleId.Contains("MEMORY_GRANT") || ruleId.Contains("SPILL")) return R_MEM;
                    if (ruleId.Contains("PARALLEL") || ruleId.Contains("SKEW")) return R_SKEW;
                    if (ruleId.Contains("RESIDUAL_PRED") || ruleId.Contains("NON_SARGABLE") || ruleId.Contains("RESIDUAL_PREDICATE")) return R_RESID;
                    if (ruleId.Contains("UDF_TVF") || ruleId.Contains("UDF")) return R_UDF;
                    if (ruleId.Contains("WAIT_STATS")) return R_WAIT;
                    if (ruleId.Contains("OPTIMIZER_ABORT")) return R_ABORT;
                    if (ruleId.Contains("RESOURCE_SEMAPHORE")) return R_SEMAPHORE;
                    if (ruleId.Contains("CACHE_RECOMPILE")) return R_CACHE;
                    if (ruleId.Contains("MISSING_INDEX")) return R_IDX;
                    if (ruleId.Contains("TABLE_SCAN")) return R_SCAN;
                    if (ruleId.Contains("HIGH_COST_OP")) return R_TOP;
                    if (ruleId.Contains("QUERY_REWRITE")) return R_REWRITE;
                    return R_PATTERN;
            }
        }

        private static bool IsNodeLevelRule(string ruleId)
        {
            if (ruleId == "RULE_016_ZERO_ROW_ACTUALS" || 
                ruleId == "RULE_017_LARGE_MEMORY_GRANT" || 
                ruleId == "RULE_035_SARGABLE_INDEX_RECOMMENDATION")
            {
                return true;
            }
            var parts = ruleId.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int idNum))
            {
                return idNum >= 1 && idNum <= 15;
            }
            return false;
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

        public static double ParseDouble(string? val)
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

        public static string ExtractObjectName(XElement relOp, XNamespace ns)
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

        public static string ExtractPredicates(XElement relOp, XNamespace ns)
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

        public static bool HasFunctionWrapper(string pred)
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

        public static string ExtractSeekPredicate(XElement relOp, XNamespace ns)
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

        public static string ExtractResidualPredicate(XElement relOp, XNamespace ns)
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

