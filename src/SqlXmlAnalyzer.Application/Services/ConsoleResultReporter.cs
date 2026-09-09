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
                    Core.Diagnostics.ExceptionPolicy.Describe(ex, "ConsoleResultReporter");
                    throw;
                }
                return;
            }

            // Console output status card
            Console.WriteLine(Core.Privacy.OutputPrivacy.RawNotice);
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
                Console.Write("Review only (No files modified)".PadRight(49));
            }
            Console.ResetColor();
            Console.WriteLine("│");
            Console.WriteLine("└──────────────────────────────────────────────────────────┘");
            Console.WriteLine($"结果状态：{DescribeOutcome(result.Outcome)}");

            if (result.Review != null) Console.WriteLine(RewriteReviewFormatter.Format(result.Review, ShowSql));
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
                    Console.WriteLine("\n[Candidate Changes]");
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
                    Console.WriteLine("\nNo candidate changes were generated.");
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
                    Console.Write(FormatSqlComparison(result));
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

        private static string DescribeOutcome(RefactorOutcome outcome) => outcome switch
        {
            RefactorOutcome.Failed => "失败，候选不可应用。",
            RefactorOutcome.Applied => "SQL 已写回。",
            RefactorOutcome.CandidateGenerated => "已生成候选 SQL，尚未应用。",
            _ => "分析完成，未产生 SQL 改写。"
        };

        private static string FormatSqlComparison(RefactorResult result)
        {
            // Both text destinations must show the reviewed selection, including the exact
            // original text for an empty selection. OutputSql is only a legacy full candidate.
            string title = result.Review == null ? "Refactored SQL" : "Selected review preview (not applied)";
            string preview = result.Review?.PreviewSql ?? result.OutputSql ?? "";
            var text = new StringBuilder();
            text.AppendLine("\n────────────────────────────────────────────────────────────");
            text.AppendLine("┌── Original SQL " + new string('─', 43));
            text.AppendLine(result.Context?.OriginalSql ?? "");
            text.AppendLine("└" + new string('─', 59));
            text.AppendLine();
            text.AppendLine("┌── " + title + " " + new string('─', Math.Max(0, 55 - title.Length)));
            text.AppendLine(preview);
            text.AppendLine("└" + new string('─', 59));
            return text.ToString();
        }

        private string FormatReportText(RefactorResult result, bool isDryRun)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Core.Privacy.OutputPrivacy.RawNotice);
            sb.AppendLine("┌──────────────────────────────────────────────────────────┐");
            sb.AppendLine("│                    REFACTORING REPORT                    │");
            sb.AppendLine("├──────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│ Status: {(result.IsSuccess ? "SUCCESS" : "FAILED").PadRight(49)}│");
            sb.AppendLine($"│ Mode:   {(isDryRun ? "Dry-Run (No files modified)" : "Review only (No files modified)").PadRight(49)}│");
            sb.AppendLine("└──────────────────────────────────────────────────────────┘");
            sb.AppendLine($"结果状态：{DescribeOutcome(result.Outcome)}");

            if (result.Review != null) sb.AppendLine(RewriteReviewFormatter.Format(result.Review, ShowSql));
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
                    sb.AppendLine("\n[Candidate Changes]");
                    foreach (var change in context.RefactorChanges)
                    {
                        sb.AppendLine($"  ● {change.RuleId,-24} : {change.Description}");
                    }
                }
                else
                {
                    sb.AppendLine("\nNo candidate changes were generated.");
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
                    sb.Append(FormatSqlComparison(result));
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
