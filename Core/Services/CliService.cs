using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using SqlXmlAnalyzer;

namespace SqlXmlAnalyzer.Core.Services
{
    public static class CliService
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        private const uint ATTACH_PARENT_PROCESS = 0x0FFFFFFFF; // -1

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        public static bool HandleCommandLineArgs(string[] args)
        {
            if (args.Length == 0) return false;

            // Simple parser
            string? inputFile = null;
            string? exportFormat = null;
            string? outputFile = null;
            bool isCliMode = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--analyze", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    inputFile = args[++i];
                    isCliMode = true;
                }
                else if (args[i].Equals("--export", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    exportFormat = args[++i].ToLowerInvariant();
                }
                else if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outputFile = args[++i];
                }
                else if (args[i].Equals("--batch", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    inputFile = args[++i]; // reuse inputFile variable as directory path
                    isCliMode = true;
                }
                else if (args[i].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[i] == "-h")
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                    PrintHelp();
                    return true; // Handled
                }
            }

            if (isCliMode)
            {
                // Attach to parent console to output text
                AttachConsole(ATTACH_PARENT_PROCESS);
                Console.WriteLine();
                Console.WriteLine($"[SqlXmlAnalyzer CLI] 启动分析...");

                try
                {
                    if (Directory.Exists(inputFile))
                    {
                        RunBatchAnalysis(inputFile, exportFormat, outputFile);
                    }
                    else
                    {
                        RunAnalysis(inputFile!, exportFormat, outputFile);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 分析过程中发生异常: {ex.Message}");
                }

                // Exit when done
                return true;
            }

            return false;
        }

        private static void RunAnalysis(string filePath, string? exportFormat, string? outputFile)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[错误] 找不到输入文件: {filePath}");
                return;
            }

            var doc = SafeXmlHelper.LoadSafe(filePath);
            bool isDeadlock = doc.Root?.Name.LocalName == "deadlock";
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            bool isPlan = doc.Root?.Name.LocalName == "ShowPlanXML";

            if (!isDeadlock && !isPlan)
            {
                Console.WriteLine($"[错误] 无法识别的文件格式，不是 deadlock 或 ShowPlanXML。");
                return;
            }

            Console.WriteLine($"[解析] 文件类型识别为: {(isDeadlock ? "死锁报告" : "执行计划")}");

            string reportText = "";
            string title = isDeadlock ? "SQL Server 死锁深度诊断报告" : "SQL Server 执行计划专家诊断报告";

            if (isDeadlock)
            {
                var parseResult = SqlXmlAnalyzer.DeadlockXmlParser.TryParseDeadlockXml(doc);
                if (!parseResult.IsSuccess || parseResult.Value == null)
                {
                    Console.WriteLine($"[错误] 死锁 XML 解析失败: {string.Join("; ", parseResult.Errors)}");
                    return;
                }
                foreach (string warning in parseResult.Warnings)
                {
                    Console.WriteLine($"[警告] {warning}");
                }
                var parsed = parseResult.Value;
                var graph = DeadlockGraphBuilder.Build(parsed.Processes, parsed.Resources, parsed.VictimId);
                var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph, doc);

                var sb = new System.Text.StringBuilder();
                foreach (var p in patterns)
                {
                    sb.AppendLine($"[{p.Severity}] {p.TypeName}");
                    sb.AppendLine($"描述: {p.Description}");
                    sb.AppendLine($"可能原因: {p.LikelyCause}");
                    sb.AppendLine($"推荐措施: {p.Recommendation}");
                    sb.AppendLine();
                }
                reportText = sb.ToString();
            }
            else
            {
                reportText = PlanDiagnosticAnalyzer.GenerateDiagnosticReport(doc, ns);
            }

            Console.WriteLine($"[成功] 分析完成！生成了 {reportText.Split('\n').Length} 行诊断报告。");

            if (!string.IsNullOrEmpty(exportFormat))
            {
                if (string.IsNullOrEmpty(outputFile))
                {
                    string extension = exportFormat == "obfuscated" ? "sqlplan" : exportFormat;
                    outputFile = Path.Combine(Environment.CurrentDirectory, $"{Path.GetFileNameWithoutExtension(filePath)}_Report.{extension}");
                }

                if (exportFormat == "pdf")
                {
                    ReportExportService.ExportToPdf(outputFile, title, reportText);
                    Console.WriteLine($"[导出] 已生成 PDF 报告: {outputFile}");
                }
                else if (exportFormat == "docx" || exportFormat == "word")
                {
                    ReportExportService.ExportToWord(outputFile, title, reportText);
                    Console.WriteLine($"[导出] 已生成 Word 报告: {outputFile}");
                }
                else if (exportFormat == "obfuscated")
                {
                    if (isPlan)
                    {
                        var obfuscated = PlanObfuscatorService.ObfuscatePlan(doc);
                        obfuscated.Save(outputFile);
                        Console.WriteLine($"[脱敏] 已生成脱敏执行计划: {outputFile}");
                    }
                    else
                    {
                        Console.WriteLine($"[警告] 脱敏仅支持执行计划文件！");
                    }
                }
                else
                {
                    // Fallback to text
                    File.WriteAllText(outputFile, reportText);
                    Console.WriteLine($"[导出] 已生成文本报告: {outputFile}");
                }
            }
            else
            {
                // Print directly to console if no export
                Console.WriteLine("\n========== 报告内容 ==========\n");
                Console.WriteLine(reportText);
                Console.WriteLine("==============================\n");
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine();
            Console.WriteLine("SqlXmlAnalyzer CLI 使用说明:");
            Console.WriteLine("  --analyze <path>   指定要分析的 .xdl 或 .sqlplan 文件");
            Console.WriteLine("  --export <format>  指定导出格式 (pdf, docx, obfuscated, txt)");
            Console.WriteLine("  --out <path>       指定输出文件路径");
            Console.WriteLine("  --help, -h         显示帮助信息");
            Console.WriteLine();
            Console.WriteLine("示例:");
            Console.WriteLine("  SqlXmlAnalyzer.exe --analyze query.sqlplan --export pdf --out report.pdf");
            Console.WriteLine("  SqlXmlAnalyzer.exe --analyze deadlock.xdl --export docx");
        }

        // Removed ParseDeadlockDocument helper method. Using SqlXmlAnalyzer.DeadlockXmlParser.ParseDeadlockXml instead.
        private static void RunBatchAnalysis(string dirPath, string? exportFormat, string? outputFile)
        {
            var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".sqlplan", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xdl", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                Console.WriteLine($"[警告] 目录 {dirPath} 中未找到 .sqlplan 或 .xdl 文件。");
                return;
            }

            Console.WriteLine($"[批处理] 找到 {files.Count} 个执行计划/死锁文件，开始自动分析...");

            int successCount = 0;
            int failCount = 0;

            foreach (var file in files)
            {
                Console.WriteLine($"\n>> 分析文件: {Path.GetFileName(file)}");
                try
                {
                    RunAnalysis(file, exportFormat, null); // Currently ignoring batch output file, outputting to console
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 分析文件失败: {ex.Message}");
                    failCount++;
                }
            }

            Console.WriteLine($"\n[批处理完成] 成功: {successCount}, 失败: {failCount}");
        }
    }
}


