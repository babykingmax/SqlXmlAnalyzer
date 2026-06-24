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
        public static List<SqlXmlAnalyzer.Core.Rules.AnalysisResult> AnalyzePlan(XDocument doc, XNamespace ns, string? configPath = null)
        {
            if (doc?.Root == null)
            {
                return new List<SqlXmlAnalyzer.Core.Rules.AnalysisResult>();
            }

            var ruleEngine = new SqlXmlAnalyzer.Core.Rules.RuleEngine(configPath);
            ruleEngine.RegisterDefaultRules();
            return ruleEngine.AnalyzePlan(doc, ns);
        }

        public static string GenerateDiagnosticReport(XDocument doc, XNamespace ns)
        {
            if (doc?.Root == null) return "⚠️ 无效的执行计划 XML 结构。";

            Logger.Info($"GenerateDiagnosticReport: 开始执行计划深度诊断 | Root={doc.Root.Name}");

            try
            {
                var reports = Enum.GetValues<SqlXmlAnalyzer.Core.Rules.RuleCategory>()
                    .ToDictionary(
                        category => category,
                        _ => new List<string>());

                var ruleResults = AnalyzePlan(doc, ns);

                foreach (var result in ruleResults)
                {
                    var metadata = result.Metadata
                        ?? throw new InvalidOperationException(
                            $"Rule result '{result.RuleId}' is missing metadata.");
                    if (metadata.Scope == SqlXmlAnalyzer.Core.Rules.RuleScope.Operator)
                    {
                        string prefix = result.Severity == "Critical" ? "❌ 严重:" : "⚠️ 警告:";
                        string msg = $"{prefix} [Node {result.NodeId}] {result.Title}\n{result.Message}";
                        reports[metadata.Category].Add(msg);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(result.Message))
                        {
                            var parts = result.Message.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var part in parts)
                            {
                                reports[metadata.Category].Add(part);
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
                        sb.AppendLine(
                            $"【{SqlXmlAnalyzer.Core.Rules.RuleMetadataCatalog.GetCategoryTitle(kv.Key)}】");
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

