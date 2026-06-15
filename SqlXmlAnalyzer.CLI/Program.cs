using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SqlXmlAnalyzer;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer.CLI
{
    public class Program
    {
        public static int Main(string[] args)
        {
            // Parse arguments
            string? path = null;
            string configPath = "RuleConfiguration.json";
            double? maxCost = null;
            bool blockScans = false;
            string format = "console";
            string? outputPath = null;
            bool showHelp = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--path":
                    case "-p":
                        if (i + 1 < args.Length) path = args[++i];
                        break;
                    case "--config":
                    case "-c":
                        if (i + 1 < args.Length) configPath = args[++i];
                        break;
                    case "--max-cost":
                    case "-m":
                        if (i + 1 < args.Length && double.TryParse(args[++i], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double costVal))
                        {
                            maxCost = costVal;
                        }
                        break;
                    case "--block-scans":
                    case "-b":
                        blockScans = true;
                        break;
                    case "--format":
                    case "-f":
                        if (i + 1 < args.Length) format = args[++i].ToLower();
                        break;
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length) outputPath = args[++i];
                        break;
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;
                }
            }

            if (showHelp || string.IsNullOrEmpty(path))
            {
                PrintUsage();
                return string.IsNullOrEmpty(path) && !showHelp ? 2 : 0;
            }

            // Collect files
            var filesToScan = new List<string>();
            if (File.Exists(path))
            {
                filesToScan.Add(path);
            }
            else if (Directory.Exists(path))
            {
                filesToScan.AddRange(Directory.GetFiles(path, "*.sqlplan", SearchOption.AllDirectories));
            }
            else
            {
                Console.Error.WriteLine($"[Error] 输入路径不存在: {path}");
                return 2;
            }

            if (filesToScan.Count == 0)
            {
                Console.WriteLine("没有找到任何 .sqlplan 文件需要扫描。");
                return 0;
            }

            var results = new List<PlanScanResult>();
            bool hasAnyFailure = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var file in filesToScan)
            {
                var fileResult = ScanPlanFile(file, configPath, maxCost, blockScans);
                results.Add(fileResult);
                if (fileResult.Status == "Failed")
                {
                    hasAnyFailure = true;
                }
            }

            sw.Stop();

            // Output formatting
            string outputContent = "";
            switch (format)
            {
                case "json":
                    outputContent = GenerateJsonOutput(results);
                    break;
                case "junit":
                    outputContent = GenerateJUnitOutput(results, sw.Elapsed.TotalSeconds);
                    break;
                default:
                    PrintConsoleOutput(results, sw.Elapsed.TotalMilliseconds);
                    break;
            }

            // Write output file if requested
            if (!string.IsNullOrEmpty(outputPath))
            {
                try
                {
                    File.WriteAllText(outputPath, outputContent, Encoding.UTF8);
                    Console.WriteLine($"报告已写入到: {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Error] 无法写入输出文件: {ex.Message}");
                    return 2;
                }
            }
            else if (format == "json" || format == "junit")
            {
                Console.WriteLine(outputContent);
            }

            return hasAnyFailure ? 1 : 0;
        }

        private static PlanScanResult ScanPlanFile(string filePath, string configPath, double? maxCostThreshold, bool blockScans)
        {
            var result = new PlanScanResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Status = "Passed"
            };

            try
            {
                XDocument doc = SafeXmlHelper.LoadSafe(filePath);
                if (doc.Root == null)
                {
                    result.Status = "Failed";
                    result.FailureMessage = "XML 根节点为空";
                    return result;
                }

                XNamespace ns = doc.Root.GetDefaultNamespace();
                if (string.IsNullOrEmpty(ns.NamespaceName))
                {
                    ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
                }

                // Analyze rules via core engine
                var ruleResults = PlanDiagnosticAnalyzer.AnalyzePlan(doc, ns, configPath);
                result.Issues = ruleResults;

                // Extract query cost
                var relOps = doc.Descendants(ns + "RelOp").ToList();
                double maxPlanCost = 0.0;
                foreach (var relOp in relOps)
                {
                    var costAttr = relOp.Attribute("EstimatedTotalSubtreeCost") ?? relOp.Attribute("SubTreeCost");
                    if (costAttr != null && double.TryParse(costAttr.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cost))
                    {
                        if (cost > maxPlanCost)
                        {
                            maxPlanCost = cost;
                        }
                    }
                }
                result.MaxSubtreeCost = maxPlanCost;

                // Check for scans
                bool containsScans = false;
                foreach (var relOp in relOps)
                {
                    var physOp = relOp.Attribute("PhysicalOp")?.Value;
                    if (physOp == "Table Scan" || physOp == "Clustered Index Scan" || physOp == "Index Scan")
                    {
                        bool hasSeek = relOp.Descendants(ns + "SeekPredicates").Any();
                        if (!hasSeek)
                        {
                            containsScans = true;
                            result.ScannedOperators.Add(new ScannedOperatorInfo
                            {
                                NodeId = relOp.Attribute("NodeId")?.Value ?? "0",
                                PhysicalOp = physOp,
                                TableName = PlanDiagnosticAnalyzer.ExtractObjectName(relOp, ns)
                            });
                        }
                    }
                }
                result.ContainsScans = containsScans;

                // Evaluate thresholds
                var failures = new List<string>();

                // 1. Check if any rule output is Critical
                var criticalRules = ruleResults.Where(r => r.Severity == "Critical").ToList();
                if (criticalRules.Count > 0)
                {
                    failures.Add($"触发 {criticalRules.Count} 个严重级别 (Critical) 的诊断规则");
                }

                // 2. Max cost threshold check
                if (maxCostThreshold.HasValue && maxPlanCost > maxCostThreshold.Value)
                {
                    failures.Add($"执行计划总开销 ({maxPlanCost:F4}) 超过设定阈值 ({maxCostThreshold.Value:F4})");
                }

                // 3. Scan check
                if (blockScans && containsScans)
                {
                    failures.Add($"检测到未被允许的扫描算子 (Table Scan / Index Scan)");
                }

                if (failures.Count > 0)
                {
                    result.Status = "Failed";
                    result.FailureMessage = string.Join("; ", failures);
                }
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.FailureMessage = $"解析执行计划异常: {ex.Message}";
            }

            return result;
        }

        private static void PrintConsoleOutput(List<PlanScanResult> results, double elapsedMs)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   SqlXmlAnalyzer CI/CD 执行计划性能回归扫描器    ");
            Console.WriteLine("==================================================");
            Console.WriteLine();

            int passed = 0;
            int failed = 0;

            foreach (var r in results)
            {
                if (r.Status == "Passed")
                {
                    passed++;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("[PASS] ");
                    Console.ResetColor();
                    Console.WriteLine($"{r.FileName} (开销: {r.MaxSubtreeCost:F4})");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("[FAIL] ");
                    Console.ResetColor();
                    Console.WriteLine($"{r.FileName} (开销: {r.MaxSubtreeCost:F4})");
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"       原因: {r.FailureMessage}");
                    Console.ResetColor();

                    if (r.ScannedOperators.Count > 0)
                    {
                        Console.WriteLine("       [扫描算子详情]");
                        foreach (var op in r.ScannedOperators)
                        {
                            Console.WriteLine($"         - Node {op.NodeId}: {op.PhysicalOp} ON {op.TableName}");
                        }
                    }

                    var criticalIssues = r.Issues.Where(i => i.Severity == "Critical" || i.Severity == "Warning").ToList();
                    if (criticalIssues.Count > 0)
                    {
                        Console.WriteLine("       [触发规则详情]");
                        foreach (var issue in criticalIssues)
                        {
                            string prefix = issue.Severity == "Critical" ? "❌ 严重" : "⚠️ 警告";
                            Console.WriteLine($"         - [{issue.RuleId}] ({prefix} Node {issue.NodeId}): {issue.Title} - {issue.Message.Replace("\n", " ")}");
                        }
                    }
                    Console.WriteLine();
                }
            }

            Console.WriteLine("--------------------------------------------------");
            Console.Write($"扫描完成. 共处理 {results.Count} 个文件 | ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"通过: {passed} ");
            Console.ResetColor();
            Console.Write("| ");
            if (failed > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"失败: {failed} ");
                Console.ResetColor();
            }
            else
            {
                Console.Write("失败: 0 ");
            }
            Console.WriteLine($"| 用时: {elapsedMs:F1}ms");
            Console.WriteLine("==================================================");
        }

        private static string GenerateJsonOutput(List<PlanScanResult> results)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return JsonSerializer.Serialize(results, options);
        }

        private static string GenerateJUnitOutput(List<PlanScanResult> results, double elapsedSeconds)
        {
            int totalTests = results.Count;
            int totalFailures = results.Count(r => r.Status == "Failed");

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine($"<testsuites>");
            sb.AppendLine($"  <testsuite name=\"SqlXmlAnalyzer.CLI\" tests=\"{totalTests}\" failures=\"{totalFailures}\" errors=\"0\" time=\"{elapsedSeconds:F3}\">");

            foreach (var r in results)
            {
                sb.AppendLine($"    <testcase name=\"{SecurityElement(r.FileName)}\" classname=\"SqlXmlAnalyzer.CLI.PlanScan\" time=\"0.000\">");
                if (r.Status == "Failed")
                {
                    sb.AppendLine($"      <failure message=\"{SecurityElement(r.FailureMessage ?? "验证失败")}\" type=\"PerformanceRegression\">");
                    sb.AppendLine("<![CDATA[");
                    sb.AppendLine($"文件路径: {r.FilePath}");
                    sb.AppendLine($"最大算子开销: {r.MaxSubtreeCost:F4}");
                    if (r.ScannedOperators.Count > 0)
                    {
                        sb.AppendLine("扫描算子列表:");
                        foreach (var op in r.ScannedOperators)
                        {
                            sb.AppendLine($"- Node {op.NodeId}: {op.PhysicalOp} ON {op.TableName}");
                        }
                    }
                    if (r.Issues.Count > 0)
                    {
                        sb.AppendLine("触发性能/分析规则:");
                        foreach (var issue in r.Issues)
                        {
                            sb.AppendLine($"- [{issue.RuleId}] ({issue.Severity} Node {issue.NodeId}): {issue.Title} - {issue.Message}");
                        }
                    }
                    sb.AppendLine("]]>");
                    sb.AppendLine("      </failure>");
                }
                sb.AppendLine("    </testcase>");
            }

            sb.AppendLine("  </testsuite>");
            sb.AppendLine("</testsuites>");
            return sb.ToString();
        }

        private static string SecurityElement(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("&", "&amp;")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;")
                        .Replace("\"", "&quot;")
                        .Replace("'", "&apos;");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法: SqlXmlAnalyzer.CLI [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  -p, --path <路径>          执行计划文件 (.sqlplan) 或包含计划文件的目录路径 (必须项)");
            Console.WriteLine("  -c, --config <路径>        规则配置文件 RuleConfiguration.json 路径 (默认为当前目录的配置)");
            Console.WriteLine("  -m, --max-cost <数值>      允许的最大执行计划子树开销限制阈值 (超过则失败)");
            Console.WriteLine("  -b, --block-scans          如果指定，检测到全表扫描/聚集索引扫描时判定为失败");
            Console.WriteLine("  -f, --format <格式>        输出报告格式: console, json, junit (默认为 console)");
            Console.WriteLine("  -o, --output <路径>        将分析报告写入指定的文件路径");
            Console.WriteLine("  -h, --help                 显示此帮助信息");
        }
    }

    public class PlanScanResult
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Status { get; set; } = "Passed"; // Passed, Failed
        public string? FailureMessage { get; set; }
        public double MaxSubtreeCost { get; set; }
        public bool ContainsScans { get; set; }
        public List<ScannedOperatorInfo> ScannedOperators { get; set; } = new();
        public List<AnalysisResult> Issues { get; set; } = new();
    }

    public class ScannedOperatorInfo
    {
        public string NodeId { get; set; } = "";
        public string PhysicalOp { get; set; } = "";
        public string TableName { get; set; } = "";
    }
}
