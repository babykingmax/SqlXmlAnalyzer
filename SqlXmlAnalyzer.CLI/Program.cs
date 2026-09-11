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
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Privacy;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.CLI
{
    public class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            SqlXmlAnalyzer.Logger.Initialize();
            using var handlers = new GlobalExceptionHandlers();
            using var cancellation = new System.Threading.CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                return Run(args, cancellation.Token);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[ERROR] {ExceptionPolicy.Describe(exception, "CLI.Main")}");
                return exception is OperationCanceledException ? 130 : 1;
            }
            finally { Console.CancelKeyPress -= cancelHandler; SqlXmlAnalyzer.Logger.Flush(); }
        }

        private static int Run(string[] args, System.Threading.CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CancelledExit();
            DocumentReadOptions readOptions;
            string? readOptionsPath = null;
            try
            {
                int optionIndex = Array.IndexOf(args, "--read-options");
                readOptions = new DocumentReadOptions();
                if (optionIndex >= 0)
                {
                    if (optionIndex + 1 >= args.Length || args[optionIndex + 1].StartsWith('-'))
                        throw new ArgumentException("--read-options 需要 JSON 文件路径。");
                    readOptionsPath = args[optionIndex + 1];
                    using var optionsStream = File.OpenRead(readOptionsPath);
                    if (optionsStream.Length > 65536) throw new ArgumentException("读取选项文件不得超过 64 KiB。");
                    readOptions = JsonSerializer.Deserialize<DocumentReadOptions>(optionsStream,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
                        ?? throw new ArgumentException("读取选项不得为 null。");
                    readOptions.Validate();
                    args = args.Take(optionIndex).Concat(args.Skip(optionIndex + 2)).ToArray();
                }
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException or IOException or UnauthorizedAccessException)
            {
                SqlXmlAnalyzer.Logger.Error("INPUT_OPTIONS_INVALID: 读取选项无效。");
                Console.Error.WriteLine("INPUT_OPTIONS_INVALID: 读取选项无效，请检查文件路径、JSON 字段及正数预算。");
                return 2;
            }
            if (args.Length > 0 && args[0] == "read")
            {
                if (args.Length != 2) { Console.Error.WriteLine("用法: read <path> [--read-options limits.json]"); return 2; }
                return RunReadCommand(args[1], readOptions, cancellationToken);
            }
            if (args.Contains("--redact", StringComparer.OrdinalIgnoreCase) || args.Contains("--redacted", StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("普通报告尚不支持脱敏共享。请使用 redact input.sqlplan --output new-redacted.sqlplan 生成脱敏计划副本。");
                return 2;
            }
            if (args.Length > 0 && args[0].Equals("redact", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 4 || args[2] != "--output")
                {
                    Console.Error.WriteLine("用法: SqlXmlAnalyzer.CLI redact input.sqlplan --output new-redacted.sqlplan");
                    return 2;
                }
                var export = new RedactedPlanExportService().ExportFile(args[1], args[3], cancellationToken);
                Console.WriteLine(JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
                return cancellationToken.IsCancellationRequested ? CancelledExit() : export.IsSuccess ? 0 : 1;
            }
            if (args.Length > 0 && args[0].Equals("refactor", StringComparison.OrdinalIgnoreCase))
            {
                return HandleRefactorCommand(args.Skip(1).ToArray(), readOptions, cancellationToken, readOptionsPath);
            }
            if (args.Length > 0 && args[0].ToLowerInvariant() is "semantic-compare" or "rewrite-validate" or "rewrite-apply")
                return SqlSemanticCommand.Run(args[0].ToLowerInvariant(), args.Skip(1).ToArray(), cancellationToken, readOptions, readOptionsPath);

            // Parse arguments
            string? path = null;
            string? configPath = null;
            double? maxCost = null;
            bool blockScans = false;
            string format = "console";
            string? outputPath = null;
            bool showHelp = false;
            var additionalExcludePatterns = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                bool requiresValue = args[i] is "--path" or "-p" or "--config" or "-c" or "--max-cost" or "-m"
                    or "--format" or "-f" or "--output" or "-o" or "--exclude" or "-x";
                if (requiresValue && (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1])
                    || args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    || args[i + 1] is "-p" or "-c" or "-m" or "-b" or "-f" or "-o" or "-x" or "-h"))
                {
                    Console.Error.WriteLine($"[Error] {args[i]} 需要参数值；请使用 --help 查看用法。");
                    return 2;
                }
                switch (args[i])
                {
                    case "--path":
                    case "-p":
                        path = args[++i];
                        break;
                    case "--config":
                    case "-c":
                        configPath = args[++i];
                        break;
                    case "--max-cost":
                    case "-m":
                        if (!TryParseCost(args[++i], out double costVal))
                        {
                            Console.Error.WriteLine("[Error] --max-cost 需要有限且非负的数值（使用小数点或科学记数法）。");
                            return 2;
                        }
                        maxCost = costVal;
                        break;
                    case "--block-scans":
                    case "-b":
                        blockScans = true;
                        break;
                    case "--format":
                    case "-f":
                        format = args[++i].ToLowerInvariant();
                        break;
                    case "--output":
                    case "-o":
                        outputPath = args[++i];
                        break;
                    case "--exclude":
                    case "-x":
                        additionalExcludePatterns.AddRange(SplitExcludePatterns(args[++i]));
                        break;
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;
                    default:
                        Console.Error.WriteLine("[Error] 不支持的扫描参数；请使用 --help 检查选项名称。");
                        return 2;
                }
            }

            if (showHelp || string.IsNullOrEmpty(path))
            {
                PrintUsage();
                return string.IsNullOrEmpty(path) && !showHelp ? 2 : 0;
            }

            if (format is not ("console" or "json" or "junit") || outputPath != null && format == "console")
            {
                Console.Error.WriteLine("[Error] --format 仅支持 console、json、junit；--output 必须搭配 json 或 junit。");
                return 2;
            }
            if (outputPath != null)
            {
                try
                {
                    outputPath = Path.GetFullPath(outputPath);
                    if (File.Exists(outputPath) || Directory.Exists(outputPath))
                        throw new IOException("请使用新的报告文件名；扫描输出不会覆盖输入或既有文件。");
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("[Error] 报告输出路径无效或已存在；请使用新的文件名。");
                    return 2;
                }
            }

            var configurationResult = RuleConfigurationLoader.Load(configPath);
            foreach (string warning in configurationResult.Warnings)
            {
                Console.Error.WriteLine($"[Warning] {warning}");
            }
            if (!configurationResult.IsSuccess)
            {
                foreach (string error in configurationResult.Errors)
                {
                    Console.Error.WriteLine($"[Error] {error}");
                }
                return 2;
            }
            configPath = configurationResult.ResolvedPath;

            // Collect files
            var filesToScan = new List<string>();
            if (File.Exists(path))
            {
                filesToScan.Add(path);
            }
            else if (Directory.Exists(path))
            {
                filesToScan.AddRange(CollectPlanFiles(path, additionalExcludePatterns, cancellationToken));
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
                var fileResult = ScanPlanFile(file, configPath, maxCost, blockScans, readOptions, cancellationToken);
                results.Add(fileResult);
                if (fileResult.Status == "Failed")
                {
                    hasAnyFailure = true;
                }
                if (fileResult.InputStatus == nameof(InputStatus.Cancelled)) break;
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
                    // Reuse exclusive, flushed temporary-file publication. This writer handles
                    // bytes only; scan reports retain their explicit raw-output privacy notice.
                    new RedactedPlanWriter().WriteNew(outputPath, new UTF8Encoding(false, true).GetBytes(outputContent), cancellationToken);
                    Console.WriteLine($"报告已写入到: {outputPath}");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return CancelledExit();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Error] 无法写入输出文件: {ExceptionPolicy.Describe(ex, "CLI.ScanReport")}");
                    return 2;
                }
            }
            else if (format == "json" || format == "junit")
            {
                Console.WriteLine(outputContent);
            }

            return cancellationToken.IsCancellationRequested ? 130 : hasAnyFailure ? 1 : 0;
        }

        public static IReadOnlyList<string> CollectPlanFiles(
            string rootPath,
            IEnumerable<string>? additionalExcludePatterns = null,
            System.Threading.CancellationToken cancellationToken = default) =>
            InputFileCollector.Collect(rootPath, [".sqlplan"], additionalExcludePatterns, cancellationToken);
        private static IEnumerable<string> SplitExcludePatterns(string value)
        {
            return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryParseCost(string value, out double cost) =>
            double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cost)
            && double.IsFinite(cost) && cost >= 0;

        internal static PlanScanResult ScanPlanFile(string filePath, string? configPath, double? maxCostThreshold, bool blockScans,
            DocumentReadOptions readOptions, System.Threading.CancellationToken cancellationToken,
            Func<RuleEngine>? ruleEngineFactory = null)
        {
            var result = new PlanScanResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Status = "Passed"
            };

            try
            {
                InputRecognitionResult input = new InputRecognitionService(options: readOptions)
                    .ReadFileAsync(filePath, cancellationToken: cancellationToken).GetAwaiter().GetResult().ForExecutionPlan();
                result.InputStatus = input.Status.ToString();
                result.InputKind = input.Kind.ToString();
                result.InputErrorCode = input.ErrorCode;
                result.InputEnvelope = input.Envelope;
                result.Capabilities = input.Capabilities.ToString();
                result.InputDiagnostics = input.Diagnostics;
                if (!input.IsSuccess)
                {
                    result.Status = "Failed";
                    result.FailureMessage = $"{input.ErrorCode}: {input.ErrorMessage}";
                    return result;
                }
                XDocument doc = input.Document!;
                XNamespace ns = doc.Root!.Name.Namespace;
                var identities = PlanIdentityAdapter.GetDocument(doc, cancellationToken);
                result.Plan = identities;

                // Analyze rules via core engine
                var diagnostics = ruleEngineFactory?.Invoke().AnalyzePlanDetailed(doc, ns, identities, input.Capabilities, cancellationToken)
                    ?? PlanDiagnosticAnalyzer.AnalyzeDetailed(doc, ns, configPath, identities, input.Capabilities, cancellationToken);
                result.Diagnostics = diagnostics;
                if (identities != null) result.DiagnosticReport = Core.Reporting.DiagnosticReportFactory.Plan(doc, identities, diagnostics, token: cancellationToken);
                var ruleResults = diagnostics.ToLegacyResults();
                cancellationToken.ThrowIfCancellationRequested();
                result.Issues = ruleResults;

                // Extract query cost
                var relOps = PlanIdentityAdapter.GetOperatorSources(doc, ns);
                double maxPlanCost = 0.0;
                int availableCosts = 0, missingCosts = 0, invalidCosts = 0;
                foreach (var relOp in relOps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var costAttr = relOp.Attribute("EstimatedTotalSubtreeCost") ?? relOp.Attribute("SubTreeCost");
                    if (costAttr == null) missingCosts++;
                    else if (TryParseCost(costAttr.Value, out double cost))
                    {
                        availableCosts++;
                        maxPlanCost = Math.Max(maxPlanCost, cost);
                    }
                    else invalidCosts++;
                }
                result.MaxSubtreeCost = maxPlanCost;
                result.MaxSubtreeCostState = invalidCosts > 0 ? "Invalid" : availableCosts == 0 ? "Missing"
                    : missingCosts > 0 ? "Incomplete" : "Available";

                // Check for scans
                bool containsScans = false;
                foreach (var relOp in relOps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var physOp = relOp.Attribute("PhysicalOp")?.Value;
                    if (physOp == "Table Scan" || physOp == "Clustered Index Scan" || physOp == "Index Scan")
                    {
                        // A descendant seek does not change this operator's PhysicalOp.
                        containsScans = true;
                        result.ScannedOperators.Add(new ScannedOperatorInfo
                        {
                            NodeId = relOp.Attribute("NodeId")?.Value ?? "0",
                            Location = identities?.FindLocation(relOp),
                            Objects = identities?.FindOperator(relOp)?.Objects ?? Array.Empty<SqlObjectReference>(),
                            PhysicalOp = physOp,
                            TableName = identities == null ? PlanDiagnosticAnalyzer.ExtractObjectName(relOp, ns)
                                : string.Join("; ", identities.FindOperator(relOp)!.Objects.Select(o => o.DisplayName))
                        });
                    }
                }
                result.ContainsScans = containsScans;

                // Evaluate thresholds
                var failures = new List<string>();
                if (diagnostics.HasFailures)
                {
                    failures.Add("RULE_EXECUTION_FAILED: " + string.Join("; ", diagnostics.Runs
                        .Where(r => r.Status == RuleRunStatus.Failed).Select(DiagnosticTextFormatter.FormatRun)));
                }

                // 1. Check if any rule output is Critical
                var criticalRules = ruleResults.Where(r => r.Run == null && r.Severity == "Critical").ToList();
                if (criticalRules.Count > 0)
                {
                    failures.Add($"触发 {criticalRules.Count} 个严重级别 (Critical) 的诊断规则");
                }

                // 2. Max cost threshold check
                if (invalidCosts > 0)
                {
                    failures.Add($"PLAN_COST_INVALID: {invalidCosts} 个算子的子树开销不是有限非负数，无法确认开销上限。");
                }
                else if (maxCostThreshold.HasValue && result.MaxSubtreeCostState != "Available")
                {
                    failures.Add("PLAN_COST_UNAVAILABLE: 缺少完整的算子子树开销，无法验证 --max-cost 阈值。");
                }
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.Status = "Failed";
                result.InputStatus = nameof(InputStatus.Cancelled);
                result.InputErrorCode = "INPUT_CANCELLED";
                result.FailureMessage = "INPUT_CANCELLED: 已取消分析。";
            }
            catch (Core.Reporting.ReportBudgetExceededException ex)
            {
                // A failed scan must not serialize the large legacy model and recreate the same amplification.
                result.Plan = null; result.Diagnostics = null; result.DiagnosticReport = null;
                result.Status = "Failed"; result.FailureMessage = ExceptionPolicy.Describe(ex, "CLI.ScanReportBudget");
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.FailureMessage = $"执行计划分析未完成: {ExceptionPolicy.Describe(ex, "CLI.ScanPlan")}";
            }

            return result;
        }

        private static void PrintConsoleOutput(List<PlanScanResult> results, double elapsedMs)
        {
            Console.WriteLine(OutputPrivacy.RawNotice);
            Console.WriteLine("==================================================");
            Console.WriteLine("   SqlXmlAnalyzer CI/CD 执行计划性能回归扫描器    ");
            Console.WriteLine("==================================================");
            Console.WriteLine();

            int passed = 0;
            int failed = 0;

            foreach (var r in results)
            {
                if (r.Diagnostics != null)
                    Console.WriteLine(DiagnosticTextFormatter.Format(r.Diagnostics));
                if (r.Status == "Passed")
                {
                    passed++;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("[PASS] ");
                    Console.ResetColor();
                    Console.WriteLine($"{r.FileName} (开销: {FormatScanCost(r)})");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("[FAIL] ");
                    Console.ResetColor();
                    Console.WriteLine($"{r.FileName} (开销: {FormatScanCost(r)})");
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"       原因: {r.FailureMessage}");
                    Console.ResetColor();

                    if (r.ScannedOperators.Count > 0)
                    {
                        Console.WriteLine("       [扫描算子详情]");
                        foreach (var op in r.ScannedOperators)
                        {
                            Console.WriteLine($"         - [{op.Location?.DisplayScope}] Node {op.NodeId}: {op.PhysicalOp} ON {op.TableName}");
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

        private static string FormatScanCost(PlanScanResult result) => result.MaxSubtreeCostState == "Available"
            ? result.MaxSubtreeCost.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
            : "N/A (" + result.MaxSubtreeCostState + ")";

        internal static string GenerateJUnitOutput(List<PlanScanResult> results, double elapsedSeconds)
        {
            int totalTests = results.Count;
            int totalFailures = results.Count(r => r.Status == "Failed");

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine($"<testsuites>");
            sb.AppendLine($"  <testsuite name=\"SqlXmlAnalyzer.CLI\" tests=\"{totalTests}\" failures=\"{totalFailures}\" errors=\"0\" time=\"{elapsedSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}\">");
            sb.AppendLine($"    <properties><property name=\"Privacy\" value=\"{SecurityElement(OutputPrivacy.RawNotice)}\" /></properties>");

            foreach (var r in results)
            {
                sb.AppendLine($"    <testcase name=\"{SecurityElement(r.FileName)}\" classname=\"SqlXmlAnalyzer.CLI.PlanScan\" time=\"0.000\">");
                if (r.Status == "Failed")
                {
                    sb.AppendLine($"      <failure message=\"{SecurityElement(r.FailureMessage ?? "验证失败")}\" type=\"{(r.Diagnostics?.HasFailures == true ? "RuleExecutionFailure" : "PerformanceRegression")}\">");
                    sb.AppendLine(SecurityElement($"文件路径: {r.FilePath}"));
                    sb.AppendLine(SecurityElement($"最大算子开销: {FormatScanCost(r)}"));
                    if (r.ScannedOperators.Count > 0)
                    {
                        sb.AppendLine("扫描算子列表:");
                        foreach (var op in r.ScannedOperators)
                        {
                            sb.AppendLine(SecurityElement($"- [{op.Location?.DisplayScope}] Node {op.NodeId}: {op.PhysicalOp} ON {op.TableName}"));
                        }
                    }
                    if (r.Issues.Count > 0)
                    {
                        sb.AppendLine("触发性能/分析规则:");
                        foreach (var issue in r.Issues)
                        {
                            sb.AppendLine(SecurityElement($"- [{issue.Location?.DisplayScope}] [{issue.RuleId}] ({issue.Severity} Node {issue.NodeId}): {issue.Title} - {issue.Message}"));
                        }
                    }
                    sb.AppendLine("      </failure>");
                }
                if (r.Diagnostics != null)
                    sb.AppendLine($"      <system-out>{SecurityElement(DiagnosticTextFormatter.Format(r.Diagnostics))}</system-out>");
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
            Console.WriteLine("语义验证与审核应用：semantic-compare / rewrite-validate / rewrite-apply；使用 <命令> --help 查看参数。");
            Console.WriteLine("读取契约: SqlXmlAnalyzer.CLI read <path> [--read-options limits.json]（XML / XEL）");
            Console.WriteLine("脱敏副本: SqlXmlAnalyzer.CLI redact input.sqlplan --output new-redacted.sqlplan");
            Console.WriteLine(OutputPrivacy.RawNotice);
            Console.WriteLine("用法: SqlXmlAnalyzer.CLI [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  -p, --path <路径>          执行计划文件 (.sqlplan) 或包含计划文件的目录路径 (必须项)");
            Console.WriteLine("  -c, --config <路径>        规则配置文件 RuleConfiguration.json 路径 (默认读取应用目录；显式相对路径基于当前目录)");
            Console.WriteLine("  -m, --max-cost <数值>      有限非负子树开销阈值 (超过或缺少完整开销证据则失败)");
            Console.WriteLine("  -b, --block-scans          检测到 Table Scan / Index Scan / Clustered Index Scan 时失败");
            Console.WriteLine("  -f, --format <格式>        输出报告格式: console, json, junit (默认为 console)");
            Console.WriteLine("  -o, --output <路径>        将 json/junit 报告保存为新文件 (禁止覆盖)");
            Console.WriteLine("      --read-options <JSON>  字节、XML 字符、深度、节点与 XEL 事件预算；Partial/失败退出 1，取消退出 130");
            Console.WriteLine("  -h, --help                 显示此帮助信息");
        }

        internal static int RunReadCommand(string path, DocumentReadOptions readOptions,
            System.Threading.CancellationToken cancellationToken, IPlanDocumentBuilder? planBuilder = null)
        {
            try
            {
                var input = new InputRecognitionService(options: readOptions)
                    .ReadFileAsync(path, cancellationToken: cancellationToken).GetAwaiter().GetResult();
                if (input.Status == InputStatus.Cancelled) return CancelledExit();
                cancellationToken.ThrowIfCancellationRequested();
                PlanDocument? plan = input.IsSuccess && input.Kind == AnalysisDocumentKind.ExecutionPlanXml
                    ? (planBuilder ?? new PlanDocumentBuilder()).Build(input.Document!, input.Envelope!, cancellationToken) : null;
                cancellationToken.ThrowIfCancellationRequested();
                string output = JsonSerializer.Serialize(new
                {
                    Privacy = OutputPrivacy.RawNotice,
                    Status = input.Status.ToString(), Kind = input.Kind.ToString(), input.IsSuccess, input.HasUsableContent,
                    input.ErrorCode, input.ErrorMessage, input.Envelope, Capabilities = input.Capabilities.ToString(), input.Diagnostics,
                    Events = input.Deadlocks.Select(e => new { e.Index, e.PayloadIndex, e.Timestamp, e.CapturedAt, e.Location }),
                    Plan = plan
                }, new JsonSerializerOptions { WriteIndented = true });
                cancellationToken.ThrowIfCancellationRequested();
                Console.WriteLine(output);
                return input.IsSuccess ? 0 : 1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CancelledExit();
            }
        }

        private static int CancelledExit()
        {
            SqlXmlAnalyzer.Logger.Warning("INPUT_CANCELLED: CLI 操作已取消。");
            return 130;
        }

        private static int HandleRefactorCommand(string[] args, DocumentReadOptions readOptions,
            System.Threading.CancellationToken cancellationToken, string? readOptionsPath = null)
        {
            if (cancellationToken.IsCancellationRequested) return CancelledExit();
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
            string? configPath = null;
            bool isDryRun = false;
            bool showSql = false;
            var selectedProposalIds = new List<string>();
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
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
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

                    case "--config":
                    case "-c":
                        if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                        {
                            Console.Error.WriteLine("[Error] --config 需要规则配置文件路径。");
                            return 2;
                        }
                        configPath = args[++i];
                        break;

                    case "--select":
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        {
                            Console.Error.WriteLine("[Error] --select 需要提案 ID；选择只生成审核预览。");
                            return 2;
                        }
                        selectedProposalIds.Add(args[++i]);
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
            string[] auxiliaryInputs = new[] { configPath ?? (planPath == null ? null : RuleConfigurationPathResolver.Resolve()), readOptionsPath }
                .OfType<string>().ToArray();
            string?[] inputPaths = [sqlPath, planPath, .. auxiliaryInputs];
            try
            {
                SqlReportPathGuard.ValidateInputs(inputPaths, outputPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"[Error] 不安全的报告输出路径：{ex.Message}");
                return 2; // Never write an error report over the SQL input either.
            }
            var services = new ServiceCollection();
            services.AddLogging(configure =>
            {
                configure.ClearProviders();
                configure.AddProvider(new ApplicationLoggerProvider());
                configure.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            });
            services.AddSingleton<IUnexpectedErrorReporter>(UnexpectedErrorReporter.Shared);

            services.AddSingleton<IFileHandler, PhysicalFileHandler>();
            services.AddSingleton<ISqlWritebackFileSystem, PhysicalSqlWritebackFileSystem>();
            services.AddSingleton<ISqlWritebackService, SqlWritebackService>();

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

            services.AddSingleton<IAnalysisEngine>(sp => new SqlXmlAnalysisEngine(configPath));
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
            services.AddSingleton(new InputRecognitionService(options: readOptions));

            using var serviceProvider = services.BuildServiceProvider();
            var orchestrator = serviceProvider.GetRequiredService<ApplicationOrchestrator>();

            var refactorOptions = new RefactorOptions(SelectedProposalIds: selectedProposalIds);
            if (maxPasses.HasValue)
            {
                refactorOptions = refactorOptions with { MaxPasses = maxPasses.Value };
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var orchestratorResult = orchestrator.Execute(sqlPath, planPath, isDryRun, refactorOptions, outputPath,
                    cancellationToken, additionalInputPaths: auxiliaryInputs);
                // Do not create a failure report after the user has cancelled.
                if (cancellationToken.IsCancellationRequested) return CancelledExit();

                if (orchestratorResult.Result == null)
                {
                    // Revalidate before writing a failure report: an alias may have appeared
                    // during analysis or writeback. Never use the SQL source as an error log.
                    SqlReportPathGuard.ValidateInputs(inputPaths, outputPath);
                    if (isJson)
                    {
                        var jsonResult = new
                        {
                            IsSuccess = false,
                            ErrorMessage = orchestratorResult.ErrorMessage,
                            Diagnostics = orchestratorResult.Diagnostics,
                            Warnings = orchestratorResult.Warnings,
                            Privacy = OutputPrivacy.RawNotice,
                            Writeback = orchestratorResult.Writeback == null ? null : new
                            {
                                Stage = orchestratorResult.Writeback.Stage.ToString(),
                                orchestratorResult.Writeback.SourceWritten,
                                orchestratorResult.Writeback.CommitOutcomeUnknown,
                                orchestratorResult.Writeback.IsCanceled,
                                orchestratorResult.Writeback.BackupVerified,
                                orchestratorResult.Writeback.BackupPath,
                                orchestratorResult.Writeback.TemporaryPath,
                                orchestratorResult.Writeback.SourceSha256,
                                orchestratorResult.Writeback.OutputSha256,
                                orchestratorResult.Writeback.Diagnostics
                            },
                            HasChanges = false,
                            SourceWritten = orchestratorResult.Writeback?.SourceWritten ?? false,
                            Outcome = "Failed",
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
                                ReportFileWriter.WriteText(outputPath, jsonString, inputPaths, cancellationToken);
                                Console.WriteLine($"报告已写入到: {outputPath}");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Error] 无法写入输出文件: {ExceptionPolicy.Describe(ex, "CLI.FailureJsonReport")}");
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
                        sb.AppendLine(OutputPrivacy.RawNotice);
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
                                ReportFileWriter.WriteText(outputPath, sb.ToString(), inputPaths, cancellationToken);
                                Console.WriteLine($"报告已写入到: {outputPath}");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Error] 无法写入输出文件: {ExceptionPolicy.Describe(ex, "CLI.FailureTextReport")}");
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Error.WriteLine(OutputPrivacy.RawNotice);
                            Console.Error.WriteLine(orchestratorResult.ErrorMessage);
                            Console.ResetColor();
                        }
                    }
                }

                return orchestratorResult.IsSuccess ? 0 : 1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CancelledExit();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[ERROR] Command execution crashed: {ExceptionPolicy.Describe(ex, "CLI.Refactor")}");
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
            Console.WriteLine("      --read-options <JSON>  辅助计划的读取预算；超限时禁止 SQL 写回");
            Console.WriteLine("  -p, --plan <路径>            关联的执行计划文件 (.sqlplan) 路径 (可选项)");
            Console.WriteLine("                               如果提供，重构引擎将结合计划中的物理开销和扫描信息进行针对性重构");
            Console.WriteLine("  -c, --config <路径>          辅助计划使用的规则配置；默认读取应用目录 RuleConfiguration.json");
            Console.WriteLine("                               rewrite-validate / rewrite-apply 须沿用 plan、config、max-passes、read-options");
            Console.WriteLine("      --select <提案ID>        选择审核预览，可重复；必须包含前序依赖，不授权写回");
            Console.WriteLine("  -d, --dry-run                兼容选项；所有 refactor 调用均仅生成审核提案，不修改原文件");
            Console.WriteLine("  -s, --show-sql               输出原始 SQL、提案 diff 与已选择的审核预览（尚未应用）");
            Console.WriteLine("  -m, --max-passes <次数>      重构引擎的最大迭代分析次数 (默认值为 5，必须是大于0的整数)");
            Console.WriteLine("  -f, --format <格式>          报告输出格式，支持: console, json (默认值为 console)");
            Console.WriteLine("  -o, --output <路径>          将重构报告写入指定的文件路径，若路径以 .json 结尾则自动切换为 json 格式");
            Console.WriteLine("                               报告先暂存再发布；禁止覆盖 SQL、辅助计划、配置及其链接");
            Console.WriteLine("  -h, --help                   显示此帮助信息");
            Console.WriteLine();
            Console.WriteLine("使用示例:");
            Console.WriteLine("  1. Dry-Run 预览模式 (推荐，不修改源文件):");
            Console.WriteLine("     SqlXmlAnalyzer.CLI refactor query.sql --dry-run");
            Console.WriteLine();
            Console.WriteLine("  2. 基础提案 (保留 query.sql 文件):");
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
        public Core.Reporting.DiagnosticReport? DiagnosticReport { get; set; }
        public PlanDiagnosticReport? Diagnostics { get; set; }
        public string Privacy => OutputPrivacy.RawNotice;
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Status { get; set; } = "Passed"; // Passed, Failed
        public string InputStatus { get; set; } = "Success";
        public string InputKind { get; set; } = "Unknown";
        public string? InputErrorCode { get; set; }
        public DocumentEnvelope? InputEnvelope { get; set; }
        public PlanDocument? Plan { get; set; }
        public string Capabilities { get; set; } = "None";
        public IReadOnlyList<InputDiagnostic> InputDiagnostics { get; set; } = Array.Empty<InputDiagnostic>();
        public string? FailureMessage { get; set; }
        public double MaxSubtreeCost { get; set; }
        public string MaxSubtreeCostState { get; set; } = "Missing";
        public bool ContainsScans { get; set; }
        public List<ScannedOperatorInfo> ScannedOperators { get; set; } = new();
        public List<AnalysisResult> Issues { get; set; } = new();
    }

    public class ScannedOperatorInfo
    {
        public string NodeId { get; set; } = "";
        public PlanLocation? Location { get; set; }
        public IReadOnlyList<SqlObjectReference> Objects { get; set; } = Array.Empty<SqlObjectReference>();
        public string PhysicalOp { get; set; } = "";
        public string TableName { get; set; } = "";
    }
}
