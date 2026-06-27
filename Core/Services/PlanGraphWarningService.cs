using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphWarningContext(
        string NodeId,
        string PhysicalOp,
        string ResidualPredicate,
        string SeekPredicate,
        bool HasActual,
        bool HasActualRead,
        double ActualRows,
        double ActualRowsRead,
        bool IsThreadDataSkewed,
        double ResidualIOThreshold,
        int ResidualIOMinRowsRead);

    public sealed record PlanGraphWarningResult(
        string WarningsText,
        string HighestSeverity);

    public sealed class PlanGraphWarningService
    {
        public PlanGraphWarningResult BuildWarnings(
            XElement relOp,
            XNamespace ns,
            PlanGraphWarningContext context,
            IEnumerable<AnalysisResult> ruleResults)
        {
            ArgumentNullException.ThrowIfNull(relOp);
            ArgumentNullException.ThrowIfNull(ruleResults);

            var warnings = new List<string>();
            AddRelOpWarnings(relOp, ns, warnings);
            AddImplicitConversionWarnings(relOp, ns, warnings);
            AddGlobalMemoryWarnings(relOp, ns, context.NodeId, warnings);
            AddRuntimeWarnings(relOp, ns, context, warnings);
            string highestSeverity = AddRuleWarnings(ruleResults, warnings);

            string warningsText = string.Join("\n• ", warnings);
            if (warnings.Count > 0)
            {
                warningsText = "• " + warningsText;
            }

            return new PlanGraphWarningResult(
                warningsText,
                highestSeverity);
        }

        private static void AddRelOpWarnings(
            XElement relOp,
            XNamespace ns,
            ICollection<string> warnings)
        {
            XElement? warningsElement = relOp.Element(ns + "Warnings");
            if (warningsElement == null)
            {
                return;
            }

            foreach (XElement warnNode in warningsElement.Elements())
            {
                string warnText = $"⚠ 操作符警告: {warnNode.Name.LocalName}";
                if (warnNode.Name.LocalName == "PlanAffectingConvert")
                {
                    string? expression = warnNode.Attribute("Expression")?.Value;
                    if (!string.IsNullOrEmpty(expression))
                    {
                        warnText += $"\n   [转换表达式]: {expression}";
                    }
                }
                else if (warnNode.Name.LocalName == "HashWarning"
                    || warnNode.Name.LocalName == "SortWarning")
                {
                    string? memoryWarning =
                        warnNode.Attribute("HashWarningDetail")?.Value
                        ?? warnNode.Attribute("SortWarningDetail")?.Value;
                    if (!string.IsNullOrEmpty(memoryWarning))
                    {
                        warnText += $" ({memoryWarning})";
                    }
                }

                warnings.Add(warnText);
            }
        }

        private static void AddImplicitConversionWarnings(
            XElement relOp,
            XNamespace ns,
            ICollection<string> warnings)
        {
            List<string> implicitConverts = relOp.Descendants(ns + "ScalarOperator")
                .Where(op => op.Attribute("ScalarString")?.Value?.Contains("CONVERT_IMPLICIT") == true)
                .Select(op => op.Attribute("ScalarString")?.Value)
                .Where(value => !string.IsNullOrEmpty(value))
                .Select(value => value!)
                .ToList();

            if (implicitConverts.Count > 0)
            {
                warnings.Add(
                    $"隐式类型转换 (CONVERT_IMPLICIT):\n   "
                    + string.Join("\n   ", implicitConverts));
            }
        }

        private static void AddGlobalMemoryWarnings(
            XElement relOp,
            XNamespace ns,
            string nodeId,
            ICollection<string> warnings)
        {
            if (nodeId != "0" && nodeId != "1")
            {
                return;
            }

            XElement? memoryGrantInfo =
                relOp.Document?.Descendants(ns + "MemoryGrantInfo").FirstOrDefault();
            if (memoryGrantInfo != null)
            {
                double granted =
                    ParseDouble(memoryGrantInfo.Attribute("GrantedMemory")?.Value);
                double used =
                    ParseDouble(memoryGrantInfo.Attribute("MaxUsedMemory")?.Value);

                if (granted > 10240 && used > 0 && used / granted < 0.1)
                {
                    warnings.Add(
                        $"内存预估过度 (申请 {granted / 1024.0:F1}MB, 仅用 {used / 1024.0:F1}MB)");
                }
                else if (granted > 0 && used > granted)
                {
                    warnings.Add(
                        $"内存不足溢出落盘 (申请 {granted / 1024.0:F1}MB, 实际需 {used / 1024.0:F1}MB)");
                }
            }

            XElement? globalWarnings =
                relOp.Document?.Descendants(ns + "Warnings").FirstOrDefault();
            XElement? memoryGrantWarning =
                globalWarnings?.Element(ns + "MemoryGrantWarning");
            if (memoryGrantWarning != null)
            {
                string type =
                    memoryGrantWarning.Attribute("GrantWarningKind")?.Value ?? string.Empty;
                warnings.Add($"内存分配警告: {type}");
            }
        }

        private static void AddRuntimeWarnings(
            XElement relOp,
            XNamespace ns,
            PlanGraphWarningContext context,
            ICollection<string> warnings)
        {
            if (context.IsThreadDataSkewed)
            {
                warnings.Add("线程数据倾斜 (Thread Data Skew)");
            }

            string? residualIoWarning =
                BuildResidualIoWarning(relOp, ns, context);
            if (residualIoWarning != null)
            {
                warnings.Add(residualIoWarning);
                return;
            }

            if (HasResidualPredicateWarning(relOp, ns, context))
            {
                warnings.Add("残差谓词寻址 (Residual Predicate)");
            }
        }

        private static string? BuildResidualIoWarning(
            XElement relOp,
            XNamespace ns,
            PlanGraphWarningContext context)
        {
            bool hasResidualPredicate =
                !string.IsNullOrEmpty(context.ResidualPredicate)
                || relOp.Elements(ns + "Predicate").Any(
                    predicate => predicate.Parent?.Name != ns + "SeekPredicate");
            if (!hasResidualPredicate
                || !context.HasActual
                || !context.HasActualRead
                || context.ActualRowsRead <= context.ResidualIOMinRowsRead
                || context.ActualRowsRead <= context.ActualRows * context.ResidualIOThreshold)
            {
                return null;
            }

            double ratio = context.ActualRows > 0
                ? context.ActualRowsRead / context.ActualRows
                : context.ActualRowsRead;

            return
                $"**残差 I/O 警告**\n" +
                $"操作符: {context.PhysicalOp}\n" +
                $"实际读取行数: {context.ActualRowsRead:N0}\n" +
                $"实际返回行数: {context.ActualRows:N0}\n" +
                $"读取/返回比: {ratio:F1} : 1\n" +
                $"说明: 该操作符因残差谓词过滤了大部分读取的行，造成大量额外 I/O。\n" +
                $"建议: 考虑将谓词改为索引列能直接查找的条件，或添加包含列的覆盖索引。";
        }

        private static bool HasResidualPredicateWarning(
            XElement relOp,
            XNamespace ns,
            PlanGraphWarningContext context)
        {
            bool hasResidualPredicate =
                !string.IsNullOrEmpty(context.ResidualPredicate)
                || relOp.Elements(ns + "Predicate").Any(
                    predicate => predicate.Parent?.Name != ns + "SeekPredicate");
            bool hasResidualString =
                context.PhysicalOp.Contains("Seek")
                && !string.IsNullOrEmpty(context.SeekPredicate)
                && !string.IsNullOrEmpty(context.ResidualPredicate);

            if (!hasResidualPredicate || !hasResidualString)
            {
                return false;
            }

            if (context.HasActual && context.HasActualRead)
            {
                return context.ActualRowsRead > context.ActualRows * 1.2
                    && context.ActualRowsRead - context.ActualRows > 100;
            }

            return true;
        }

        private static string AddRuleWarnings(
            IEnumerable<AnalysisResult> ruleResults,
            ICollection<string> warnings)
        {
            string highestSeverity = "Info";
            foreach (AnalysisResult result in ruleResults)
            {
                warnings.Add($"[{result.Severity}] {result.Title}: {result.Message}");
                if (result.Severity == "Critical")
                {
                    highestSeverity = "Critical";
                }
                else if (result.Severity == "Warning"
                    && highestSeverity != "Critical")
                {
                    highestSeverity = "Warning";
                }
            }

            return highestSeverity;
        }

        private static double ParseDouble(
            string? value,
            double defaultValue = 0.0)
        {
            return double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : defaultValue;
        }
    }
}
