using System;
using System.IO;
using System.Text;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Application.Services
{
    public class ConsoleResultReporter : IResultReporter
    {
        public bool ShowSql { get; set; }

        public void Report(RefactorResult result)
        {
            Report(result, false, null);
        }

        public void Report(RefactorResult result, bool isDryRun, string? outputPath = null)
        {
            if (result == null) return;

            if (!string.IsNullOrEmpty(outputPath))
            {
                var fileContent = FormatReportText(result, isDryRun);
                try
                {
                    File.WriteAllText(outputPath, fileContent, Encoding.UTF8);
                    Console.WriteLine($"报告已写入到: {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"[Error] 无法写入输出文件: {ex.Message}");
                    Console.ResetColor();
                }
                return;
            }

            // Console output status card
            Console.WriteLine("┌──────────────────────────────────────────────────────────┐");
            Console.WriteLine("│                    REFACTORING REPORT                    │");
            Console.WriteLine("├──────────────────────────────────────────────────────────┤");
            
            // Status line
            Console.Write("│ Status: ");
            if (result.IsSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("SUCCESS".PadRight(49));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("FAILED".PadRight(49));
            }
            Console.ResetColor();
            Console.WriteLine("│");

            // Mode line
            Console.Write("│ Mode:   ");
            if (isDryRun)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Dry-Run (No files modified)".PadRight(49));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Normal (Changes written to disk)".PadRight(49));
            }
            Console.ResetColor();
            Console.WriteLine("│");
            Console.WriteLine("└──────────────────────────────────────────────────────────┘");

            var context = result.Context;
            if (context != null)
            {
                // Warnings output
                if (context.Warnings.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[Warnings]");
                    Console.ResetColor();
                    foreach (var warning in context.Warnings)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  ! {warning}");
                        Console.ResetColor();
                    }
                    Console.WriteLine("────────────────────────────────────────────────────────────");
                }

                // Applied Changes formatting (Rule ID + Description)
                if (context.RefactorChanges.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("\n[Applied Changes]");
                    Console.ResetColor();
                    foreach (var change in context.RefactorChanges)
                    {
                        Console.Write("  ● ");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"{change.RuleId,-24}");
                        Console.ResetColor();
                        Console.WriteLine($" : {change.Description}");
                    }
                }
                else
                {
                    Console.WriteLine("\nNo refactoring changes were applied.");
                }

                // Rule execution failures
                if (context.RefactorFailures.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[Failures/Errors]");
                    Console.ResetColor();
                    foreach (var failure in context.RefactorFailures)
                    {
                        Console.Write("  ● ");
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write($"{failure.RuleId,-24}");
                        Console.ResetColor();
                        Console.WriteLine($" : {failure.ExceptionName} - {failure.StackTrace}");
                    }
                }

                // SQL Parse Errors with visual marker/caret
                if (result.ParseErrors != null && result.ParseErrors.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[SQL Parse Errors]");
                    Console.ResetColor();
                    var sqlLines = (context.OriginalSql ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (var err in result.ParseErrors)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  Line {err.Line}, Col {err.Column}: {err.Message}");
                        Console.ResetColor();

                        int lineIndex = err.Line - 1;
                        if (lineIndex >= 0 && lineIndex < sqlLines.Length)
                        {
                            string lineText = sqlLines[lineIndex];
                            Console.Write("    Code: ");

                            int errCol = err.Column;
                            if (errCol > 0 && errCol - 1 <= lineText.Length)
                            {
                                string before = lineText.Substring(0, errCol - 1);
                                string errChar = errCol - 1 < lineText.Length ? lineText.Substring(errCol - 1, 1) : " ";
                                string after = errCol < lineText.Length ? lineText.Substring(errCol) : "";

                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.Write(before);

                                Console.BackgroundColor = ConsoleColor.Red;
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.Write(errChar);
                                Console.ResetColor();

                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.WriteLine(after);
                                Console.ResetColor();
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine(lineText);
                                Console.ResetColor();
                            }

                            Console.Write("          ");
                            Console.ForegroundColor = ConsoleColor.Red;
                            var caretLine = new StringBuilder();
                            for (int col = 1; col < err.Column; col++)
                            {
                                caretLine.Append(col - 1 < lineText.Length && lineText[col - 1] == '\t' ? "    " : " ");
                            }
                            caretLine.Append("^");
                            Console.WriteLine(caretLine.ToString());
                            Console.ResetColor();
                        }
                    }
                }
                else if (result.Errors != null && result.Errors.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[Engine Errors]");
                    Console.ResetColor();
                    foreach (var error in result.Errors)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  - {error}");
                        Console.ResetColor();
                    }
                }

                // SQL Comparison formatting
                if (ShowSql)
                {
                    Console.WriteLine("\n────────────────────────────────────────────────────────────");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("┌── Original SQL ───────────────────────────────────────────");
                    Console.ResetColor();
                    Console.WriteLine((context.OriginalSql ?? "").Trim());
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("└" + new string('─', 59));
                    Console.ResetColor();

                    Console.WriteLine();

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("┌── Refactored SQL ─────────────────────────────────────────");
                    Console.ResetColor();
                    Console.WriteLine((result.OutputSql ?? "").Trim());
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("└" + new string('─', 59));
                    Console.ResetColor();
                }
                else if (isDryRun)
                {
                    Console.WriteLine("\n────────────────────────────────────────────────────────────");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("💡 Tip: ");
                    Console.ResetColor();
                    Console.WriteLine("Use the --show-sql / -s option to view the full SQL comparison.");
                }
            }

            Console.WriteLine("────────────────────────────────────────────────────────────");
        }

        private string FormatReportText(RefactorResult result, bool isDryRun)
        {
            var sb = new StringBuilder();
            sb.AppendLine("┌──────────────────────────────────────────────────────────┐");
            sb.AppendLine("│                    REFACTORING REPORT                    │");
            sb.AppendLine("├──────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│ Status: {(result.IsSuccess ? "SUCCESS" : "FAILED").PadRight(49)}│");
            sb.AppendLine($"│ Mode:   {(isDryRun ? "Dry-Run (No files modified)" : "Normal (Changes written to disk)").PadRight(49)}│");
            sb.AppendLine("└──────────────────────────────────────────────────────────┘");

            var context = result.Context;
            if (context != null)
            {
                if (context.Warnings.Count > 0)
                {
                    sb.AppendLine("\n[Warnings]");
                    foreach (var warning in context.Warnings)
                    {
                        sb.AppendLine($"  ! {warning}");
                    }
                    sb.AppendLine("────────────────────────────────────────────────────────────");
                }

                if (context.RefactorChanges.Count > 0)
                {
                    sb.AppendLine("\n[Applied Changes]");
                    foreach (var change in context.RefactorChanges)
                    {
                        sb.AppendLine($"  ● {change.RuleId,-24} : {change.Description}");
                    }
                }
                else
                {
                    sb.AppendLine("\nNo refactoring changes were applied.");
                }

                if (context.RefactorFailures.Count > 0)
                {
                    sb.AppendLine("\n[Failures/Errors]");
                    foreach (var failure in context.RefactorFailures)
                    {
                        sb.AppendLine($"  ● {failure.RuleId,-24} : {failure.ExceptionName} - {failure.StackTrace}");
                    }
                }

                if (result.ParseErrors != null && result.ParseErrors.Count > 0)
                {
                    sb.AppendLine("\n[SQL Parse Errors]");
                    var sqlLines = (context.OriginalSql ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (var err in result.ParseErrors)
                    {
                        sb.AppendLine($"  Line {err.Line}, Col {err.Column}: {err.Message}");
                        int lineIndex = err.Line - 1;
                        if (lineIndex >= 0 && lineIndex < sqlLines.Length)
                        {
                            string lineText = sqlLines[lineIndex];
                            sb.AppendLine($"    Code: {lineText}");
                            var caretLine = new StringBuilder();
                            for (int col = 1; col < err.Column; col++)
                            {
                                caretLine.Append(col - 1 < lineText.Length && lineText[col - 1] == '\t' ? "    " : " ");
                            }
                            caretLine.Append("^");
                            sb.AppendLine($"          {caretLine}");
                        }
                    }
                }
                else if (result.Errors != null && result.Errors.Count > 0)
                {
                    sb.AppendLine("\n[Engine Errors]");
                    foreach (var error in result.Errors)
                    {
                        sb.AppendLine($"  - {error}");
                    }
                }

                if (ShowSql)
                {
                    sb.AppendLine("\n────────────────────────────────────────────────────────────");
                    sb.AppendLine("┌── Original SQL " + new string('─', 43));
                    sb.AppendLine((context.OriginalSql ?? "").Trim());
                    sb.AppendLine("└" + new string('─', 59));
                    sb.AppendLine();
                    sb.AppendLine("┌── Refactored SQL " + new string('─', 41));
                    sb.AppendLine((result.OutputSql ?? "").Trim());
                    sb.AppendLine("└" + new string('─', 59));
                }
                else if (isDryRun)
                {
                    sb.AppendLine("\n────────────────────────────────────────────────────────────");
                    sb.AppendLine("Tip: Use the --show-sql / -s option to view the full SQL comparison.");
                }
            }

            sb.AppendLine("────────────────────────────────────────────────────────────");
            return sb.ToString();
        }
    }
}
