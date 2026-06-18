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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Refactoring.Rules;

namespace SqlXmlAnalyzer.CLI
{
    public class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                var initField = typeof(SqlXmlAnalyzer.Logger).GetField("_initialized", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (initField != null)
                {
                    initField.SetValue(null, false);
                }
                SqlXmlAnalyzer.Logger.Initialize(logLevel: SqlXmlAnalyzer.LogLevel.Error, enableFileLogging: false);
            }
            catch
            {
                // Ignore logger init error
            }

            if (args.Length > 0 && args[0].Equals("refactor", StringComparison.OrdinalIgnoreCase))
            {
                return HandleRefactorCommand(args.Skip(1).ToArray());
            }

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

        private static int HandleRefactorCommand(string[] args)
        {
            if (args.Contains("--help") || args.Contains("-h"))
            {
                PrintRefactorUsage();
                return 0;
            }

            if (args.Length == 0)
            {
                Console.Error.WriteLine("[Error] 参数错误: 未指定 SQL 文件路径。");
                Console.WriteLine();
                PrintRefactorUsage();
                return 2;
            }

            string sqlPath = args[0];
            if (sqlPath.StartsWith("-"))
            {
                Console.Error.WriteLine($"[Error] 参数错误: SQL 文件路径不能以 '-' 开头: '{sqlPath}'。若要查看帮助，请使用 --help。");
                Console.WriteLine();
                PrintRefactorUsage();
                return 2;
            }

            string? planPath = null;
            bool isDryRun = false;
            bool showSql = false;
            int? maxPasses = null;
            string format = "console";
            string? outputPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--plan":
                    case "-p":
                        if (i + 1 < args.Length)
                        {
                            planPath = args[++i];
                        }
                        else
                        {
                            Console.Error.WriteLine($"[Error] 参数错误: 选项 '{arg}' 缺少参数值。");
                            return 2;
                        }
                        break;

                    case "--dry-run":
                    case "-d":
                        isDryRun = true;
                        break;

                    case "--show-sql":
                    case "-s":
                        showSql = true;
                        break;

                    case "--max-passes":
                    case "-m":
                        if (i + 1 < args.Length)
                        {
                            var val = args[++i];
                            if (int.TryParse(val, out int passes) && passes > 0)
                            {
                                maxPasses = passes;
                            }
                            else
                            {
                                Console.Error.WriteLine($"[Error] 参数错误: 选项 '{arg}' 的值必须是大于 0 的有效整数，当前值为 '{val}'。");
                                return 2;
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine($"[Error] 参数错误: 选项 '{arg}' 缺少参数值。");
                            return 2;
                        }
                        break;

                    case "--format":
                    case "-f":
                        if (i + 1 < args.Length)
                        {
                            var val = args[++i];
                            if (val.Equals("console", StringComparison.OrdinalIgnoreCase) ||
                                val.Equals("json", StringComparison.OrdinalIgnoreCase))
                            {
                                format = val.ToLower();
                            }
                            else
                            {
                                Console.Error.WriteLine($"[Error] 参数错误: 不支持的输出格式 '{val}'。支持的格式为: console, json。");
                                return 2;
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine($"[Error] 参数错误: 选项 '{arg}' 缺少参数值。");
                            return 2;
                        }
                        break;

                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length)
                        {
                            var val = args[++i];
                            if (val.Equals("json", StringComparison.OrdinalIgnoreCase))
                            {
                                format = "json";
                            }
                            else
                            {
                                outputPath = val;
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine($"[Error] 参数错误: 选项 '{arg}' 缺少参数值。");
                            return 2;
                        }
                        break;

                    default:
                        Console.Error.WriteLine($"[Error] 参数错误: 未知的选项或参数 '{arg}'。");
                        Console.WriteLine();
                        PrintRefactorUsage();
                        return 2;
                }
            }

            // Setup services
            var services = new ServiceCollection();
            services.AddLogging(configure =>
            {
                configure.AddConsole(options =>
                {
                    options.LogToStandardErrorThreshold = Microsoft.Extensions.Logging.LogLevel.Trace;
                });
                configure.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
            });

            services.AddSingleton<IFileHandler, PhysicalFileHandler>();

            bool isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase) || 
                          (!string.IsNullOrEmpty(outputPath) && outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

            if (isJson)
            {
                services.AddSingleton<IResultReporter>(sp => new JsonResultReporter { ShowSql = showSql });
            }
            else
            {
                services.AddSingleton<IResultReporter>(sp => new ConsoleResultReporter { ShowSql = showSql });
            }

            services.AddSingleton<IAnalysisEngine>(sp => new SqlXmlAnalysisEngine("RuleConfiguration.json"));
            services.AddSingleton<IRuleFilter, DefaultRuleFilter>();

            // Rules
            services.AddSingleton<ISqlRefactorRule, ConstantFoldingRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, IsNullComparisonRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, LeftOrSubstringRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, TrimRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, ImplicitConversionRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, SubqueryToJoinRule>();
            services.AddSingleton<ISqlRefactorRule, ExistsToJoinRule>();
            services.AddSingleton<ISqlRefactorRule, TableVariableRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, ScalarSubqueryToJoinRule>();

            services.AddSingleton<IRefactoringEngine, SqlRefactoringEngine>();
            services.AddSingleton<ApplicationOrchestrator>();

            using var serviceProvider = services.BuildServiceProvider();
            var orchestrator = serviceProvider.GetRequiredService<ApplicationOrchestrator>();

            var refactorOptions = new RefactorOptions();
            if (maxPasses.HasValue)
            {
                refactorOptions = refactorOptions with { MaxPasses = maxPasses.Value };
            }

            try
            {
                var orchestratorResult = orchestrator.Execute(sqlPath, planPath, isDryRun, refactorOptions, outputPath);

                if (orchestratorResult.Result == null)
                {
                    // Handle failure before refactoring engine ran
                    if (isJson)
                    {
                        var jsonResult = new
                        {
                            IsSuccess = false,
                            ErrorMessage = orchestratorResult.ErrorMessage,
                            Warnings = orchestratorResult.Warnings,
                            HasChanges = false,
                            Changes = new List<object>(),
                            Failures = new List<object>(),
                            WarningsList = new List<object>(),
                            Errors = new List<string> { orchestratorResult.ErrorMessage ?? "Unknown orchestration error" },
                            OriginalSql = (string?)null,
                            RefactoredSql = (string?)null
                        };

                        var jsonOptions = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        string jsonString = JsonSerializer.Serialize(jsonResult, jsonOptions);

                        if (!string.IsNullOrEmpty(outputPath))
                        {
                            try
                            {
                                File.WriteAllText(outputPath, jsonString, Encoding.UTF8);
                                Console.WriteLine($"报告已写入到: {outputPath}");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Error] 无法写入输出文件: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine(jsonString);
                        }
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("==================================================");
                        sb.AppendLine("                Refactoring Report                ");
                        sb.AppendLine("==================================================");
                        sb.AppendLine("[FAILED] Refactoring failed.");
                        sb.AppendLine(orchestratorResult.ErrorMessage);
                        sb.AppendLine("==================================================");

                        if (!string.IsNullOrEmpty(outputPath))
                        {
                            try
                            {
                                File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
                                Console.WriteLine($"报告已写入到: {outputPath}");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Error] 无法写入输出文件: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Error.WriteLine(orchestratorResult.ErrorMessage);
                            Console.ResetColor();
                        }
                    }
                }

                return orchestratorResult.IsSuccess ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[ERROR] Command execution crashed: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static void PrintRefactorUsage()
        {
            Console.WriteLine("用法: SqlXmlAnalyzer.CLI refactor <SQL文件路径> [选项]");
            Console.WriteLine();
            Console.WriteLine("对指定的 SQL 文件进行自动重构优化，识别并修复潜在的性能问题和不规范写法。");
            Console.WriteLine();
            Console.WriteLine("参数:");
            Console.WriteLine("  <SQL文件路径>                需要重构的目标 SQL 文件路径 (必须项)");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  -p, --plan <路径>            关联的执行计划文件 (.sqlplan) 路径 (可选项)");
            Console.WriteLine("                               如果提供，重构引擎将结合计划中的物理开销和扫描信息进行针对性重构");
            Console.WriteLine("  -d, --dry-run                Dry-Run 模式，仅分析并输出重构变更摘要，不修改原文件");
            Console.WriteLine("  -s, --show-sql               在 Dry-Run 模式下同时输出重构前后的完整 SQL 对比");
            Console.WriteLine("  -m, --max-passes <次数>      重构引擎的最大迭代分析次数 (默认值为 5，必须是大于0的整数)");
            Console.WriteLine("  -f, --format <格式>          报告输出格式，支持: console, json (默认值为 console)");
            Console.WriteLine("  -o, --output <路径>          将重构报告写入指定的文件路径，若路径以 .json 结尾则自动切换为 json 格式");
            Console.WriteLine("  -h, --help                   显示此帮助信息");
            Console.WriteLine();
            Console.WriteLine("使用示例:");
            Console.WriteLine("  1. Dry-Run 预览模式 (推荐，不修改源文件):");
            Console.WriteLine("     SqlXmlAnalyzer.CLI refactor query.sql --dry-run");
            Console.WriteLine();
            Console.WriteLine("  2. 基础重构 (直接修改 query.sql 文件):");
            Console.WriteLine("     SqlXmlAnalyzer.CLI refactor query.sql");
            Console.WriteLine();
            Console.WriteLine("  3. 结合执行计划进行重构:");
            Console.WriteLine("     SqlXmlAnalyzer.CLI refactor query.sql --plan query.sqlplan");
            Console.WriteLine();
            Console.WriteLine("  4. 输出 JSON 格式报告到指定文件:");
            Console.WriteLine("     SqlXmlAnalyzer.CLI refactor query.sql --output report.json");
            Console.WriteLine();
            Console.WriteLine("  5. 使用 --show-sql 查看完整 SQL 对比:");
            Console.WriteLine("     SqlXmlAnalyzer.CLI refactor query.sql --dry-run --show-sql");
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
