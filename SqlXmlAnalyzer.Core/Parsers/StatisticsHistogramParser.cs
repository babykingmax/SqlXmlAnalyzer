using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Parsers
{
    public static class StatisticsHistogramParser
    {
        public static List<HistogramStep>? Parse(string text, out HistogramKeyType keyType)
        {
            keyType = HistogramKeyType.Numeric;
            if (string.IsNullOrWhiteSpace(text)) return null;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var headers = lines[0].Split('\t').Select(h => h.Trim().ToUpperInvariant()).ToList();
            int colKey = headers.IndexOf("RANGE_HI_KEY"), colRange = headers.IndexOf("RANGE_ROWS"), colEq = headers.IndexOf("EQ_ROWS");
            int colDistinct = headers.IndexOf("DISTINCT_RANGE_ROWS"), colAverage = headers.IndexOf("AVG_RANGE_ROWS");
            bool hasHeader = headers.Any(h => h is "RANGE_HI_KEY" or "RANGE_ROWS" or "EQ_ROWS" or "DISTINCT_RANGE_ROWS" or "AVG_RANGE_ROWS");
            if (hasHeader && (colKey < 0 || colRange < 0 || colEq < 0)) return null;
            if (!hasHeader)
            {
                if (headers.Count < 3) return null;
                (colKey, colRange, colEq, colDistinct, colAverage) = (0, 1, 2, 3, 4);
            }
            var steps = new List<HistogramStep>();
            for (int i = hasHeader ? 1 : 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length <= colKey) return null;
                double range = ReadRows(parts, colRange), eq = ReadRows(parts, colEq);
                if (!double.IsFinite(range) || !double.IsFinite(eq)) return null;
                double distinct = ReadRows(parts, colDistinct), average = ReadRows(parts, colAverage);
                if (!double.IsFinite(average) && double.IsFinite(distinct) && distinct > 0) average = range / distinct;
                steps.Add(new HistogramStep
                {
                    RangeHiKey = parts[colKey], RangeRows = range, EqRows = eq,
                    DistinctRangeRows = distinct, AvgRangeRows = average
                });
            }
            if (steps.Count == 0) return null;
            // SQL NULL does not change the type inferred from the remaining keys.
            var keys = steps.Where(s => !s.IsNull).ToList();
            if (keys.All(s => ExactNumber.TryParse(s.RangeHiKey, out _))) keyType = HistogramKeyType.Numeric;
            else if (keys.All(s => TryDate(s.RangeHiKey, out _))) keyType = HistogramKeyType.DateTime;
            else keyType = HistogramKeyType.String;
            // These doubles are drawing coordinates only, never key comparison values.
            foreach (var step in steps)
            {
                step.RangeHiKeyNumeric = double.NaN;
                if (step.IsNull) continue;
                if (keyType == HistogramKeyType.Numeric)
                    step.RangeHiKeyNumeric = double.TryParse(step.RangeHiKey, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
                else if (keyType == HistogramKeyType.DateTime && TryDate(step.RangeHiKey, out var date)) step.RangeHiKeyNumeric = date.Ticks;
            }
            bool ordinal = keyType == HistogramKeyType.String || keys.Any(s => !double.IsFinite(s.RangeHiKeyNumeric)) ||
                keys.Zip(keys.Skip(1), (a, b) => b.RangeHiKeyNumeric <= a.RangeHiKeyNumeric).Any(x => x) ||
                (keys.Count > 0 && !double.IsFinite(keys[^1].RangeHiKeyNumeric - keys[0].RangeHiKeyNumeric));
            if (ordinal)
                for (int i = 0; i < keys.Count; i++) keys[i].RangeHiKeyNumeric = i;
            return steps;
        }

        /// <summary>Unknown estimates/positions are NaN. This illustrates a histogram, not SQL Server's cardinality estimator.</summary>
        public static void EstimateValue(string? valStr, List<HistogramStep> steps, HistogramKeyType keyType,
            out double estimatedRows, out double numericPosition, out string matchType)
        {
            estimatedRows = numericPosition = double.NaN;
            matchType = "未知：参数缺失或无法解析";
            if (steps == null || steps.Count == 0 || valStr == null ||
                (keyType != HistogramKeyType.String && string.IsNullOrWhiteSpace(valStr)) ||
                string.Equals(valStr, "NULL", StringComparison.Ordinal)) return;
            var keys = steps.Where(s => !s.IsNull).ToList();
            if (keys.Count == 0) return;
            if (keyType == HistogramKeyType.String)
            {
                var exact = keys.FirstOrDefault(s => string.Equals(s.RangeHiKey, valStr, StringComparison.Ordinal));
                if (exact == null)
                {
                    matchType = "未知：未提供 SQL Server 排序规则，无法判断字符串区间";
                    return;
                }
                estimatedRows = exact.EqRows;
                numericPosition = exact.RangeHiKeyNumeric;
                matchType = "精确匹配 EQ_ROWS";
                return;
            }
            var comparisons = new List<int>(keys.Count);
            double approximate;
            if (keyType == HistogramKeyType.Numeric)
            {
                if (!ExactNumber.TryParse(valStr, out var value)) return;
                foreach (var key in keys)
                {
                    if (!ExactNumber.TryParse(key.RangeHiKey, out var boundary)) return;
                    comparisons.Add(boundary.CompareTo(value));
                }
                approximate = double.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
            }
            else
            {
                if (!TryDate(valStr, out var value)) return;
                foreach (var key in keys)
                {
                    if (!TryDate(key.RangeHiKey, out var boundary)) return;
                    comparisons.Add(boundary.Ticks.CompareTo(value.Ticks));
                }
                approximate = value.Ticks;
            }
            int index = comparisons.FindIndex(c => c >= 0);
            if (index >= 0 && comparisons[index] == 0)
            {
                estimatedRows = keys[index].EqRows;
                numericPosition = keys[index].RangeHiKeyNumeric;
                matchType = "精确匹配 EQ_ROWS";
                return;
            }
            var step = index < 0 ? keys[^1] : keys[index];
            estimatedRows = step.AvgRangeRows;
            if (!double.IsFinite(estimatedRows))
            {
                matchType = "未知：缺少 AVG_RANGE_ROWS";
                return;
            }
            matchType = index < 0 ? "超出直方图上限，取最后区间 AVG_RANGE_ROWS" :
                index == 0 ? "低于直方图下限，取首个区间 AVG_RANGE_ROWS" : "落入区间, 取平均行数 AVG_RANGE_ROWS";
            if (index < 0) numericPosition = keys[^1].RangeHiKeyNumeric;
            else if (index == 0) numericPosition = keys[0].RangeHiKeyNumeric;
            else
            {
                double lower = keys[index - 1].RangeHiKeyNumeric, upper = step.RangeHiKeyNumeric;
                numericPosition = double.IsFinite(approximate) && approximate > lower && approximate < upper
                    ? approximate : lower / 2 + upper / 2;
            }
        }

        private static bool TryDate(string text, out DateTime value) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

        private static double ReadRows(string[] parts, int index) => index >= 0 && index < parts.Length &&
            double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0
            ? value : double.NaN;

        // Preserve SQL decimal(38, s), bigint and scientific notation without double/decimal rounding.
        // Length/exponent limits bound BigInteger work for untrusted pasted input.
        private readonly record struct ExactNumber(BigInteger Coefficient, int Exponent) : IComparable<ExactNumber>
        {
            public int CompareTo(ExactNumber other)
            {
                int common = Math.Min(Exponent, other.Exponent);
                return (Coefficient * BigInteger.Pow(10, Exponent - common))
                    .CompareTo(other.Coefficient * BigInteger.Pow(10, other.Exponent - common));
            }

            public static bool TryParse(string text, out ExactNumber value)
            {
                value = default;
                text = text.Trim();
                if (text.Length == 0 || text.Length > 512) return false;
                int exponent = 0, e = text.IndexOfAny(new[] { 'e', 'E' });
                if (e >= 0)
                {
                    if (!int.TryParse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent) ||
                        exponent < -400 || exponent > 400) return false;
                    text = text[..e];
                }
                bool negative = text.StartsWith('-');
                if (text.StartsWith('+') || negative) text = text[1..];
                int dot = text.IndexOf('.');
                if (dot >= 0)
                {
                    exponent -= text.Length - dot - 1;
                    text = text.Remove(dot, 1);
                }
                if (text.Length == 0 || text.Any(c => c < '0' || c > '9') ||
                    !BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var coefficient)) return false;
                value = new ExactNumber(negative ? -coefficient : coefficient, exponent);
                return true;
            }
        }
    }
}
