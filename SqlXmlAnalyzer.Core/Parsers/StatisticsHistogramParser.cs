using System;
using System.Collections.Generic;
using System.Linq;
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
            if (lines.Length < 1) return null;

            // Find headers
            var headerLine = lines[0];
            var headers = headerLine.Split('\t').Select(h => h.Trim().ToUpper()).ToList();

            int colKey = headers.IndexOf("RANGE_HI_KEY");
            int colRangeRows = headers.IndexOf("RANGE_ROWS");
            int colEqRows = headers.IndexOf("EQ_ROWS");
            int colDistinctRangeRows = headers.IndexOf("DISTINCT_RANGE_ROWS");
            int colAvgRangeRows = headers.IndexOf("AVG_RANGE_ROWS");

            // Fallback to position-based if headers not matched
            bool hasHeader = colKey >= 0 || colRangeRows >= 0 || colEqRows >= 0;
            int startRow = 1;

            if (!hasHeader)
            {
                if (lines[0].Split('\t').Length < 3) return null;

                colKey = 0;
                colRangeRows = 1;
                colEqRows = 2;
                colDistinctRangeRows = 3;
                colAvgRangeRows = 4;
                startRow = 0; // No header, start from first row
            }

            var steps = new List<HistogramStep>();
            bool allNumeric = true;
            bool allDateTime = true;

            for (int i = startRow; i < lines.Length; i++)
            {
                var line = lines[i];
                var parts = line.Split('\t');
                if (parts.Length <= colKey) continue;

                string rawKey = parts[colKey].Trim();
                if (string.IsNullOrEmpty(rawKey)) continue;

                double rRows = 0, eqRows = 0, distRows = 0, avgRows = 0;

                if (parts.Length > colRangeRows) double.TryParse(parts[colRangeRows], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rRows);
                if (parts.Length > colEqRows) double.TryParse(parts[colEqRows], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out eqRows);
                if (parts.Length > colDistinctRangeRows) double.TryParse(parts[colDistinctRangeRows], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out distRows);
                if (parts.Length > colAvgRangeRows) double.TryParse(parts[colAvgRangeRows], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out avgRows);

                // Check key type
                if (allNumeric && !double.TryParse(rawKey, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    allNumeric = false;
                }
                if (allDateTime && !DateTime.TryParse(rawKey, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    allDateTime = false;
                }

                steps.Add(new HistogramStep
                {
                    RangeHiKey = rawKey,
                    RangeRows = rRows,
                    EqRows = eqRows,
                    DistinctRangeRows = distRows,
                    AvgRangeRows = avgRows
                });
            }

            if (steps.Count == 0) return null;

            if (allNumeric) keyType = HistogramKeyType.Numeric;
            else if (allDateTime) keyType = HistogramKeyType.DateTime;
            else keyType = HistogramKeyType.String;

            // Assign numeric representations
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (keyType == HistogramKeyType.Numeric)
                {
                    double.TryParse(s.RangeHiKey, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val);
                    s.RangeHiKeyNumeric = val;
                }
                else if (keyType == HistogramKeyType.DateTime)
                {
                    DateTime.TryParse(s.RangeHiKey, System.Globalization.CultureInfo.InvariantCulture, out DateTime dt);
                    s.RangeHiKeyNumeric = dt.Ticks;
                }
                else
                {
                    s.RangeHiKeyNumeric = i; // index based for strings
                }
            }

            return steps;
        }

        public static void EstimateValue(string valStr, List<HistogramStep> steps, HistogramKeyType keyType, out double estimatedRows, out double numericPosition, out string matchType)
        {
            estimatedRows = 0.0;
            numericPosition = 0.0;
            matchType = "未匹配";

            if (steps == null || steps.Count == 0) return;

            double valNum = 0;
            bool parseOk = false;

            if (keyType == HistogramKeyType.Numeric)
            {
                parseOk = double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out valNum);
            }
            else if (keyType == HistogramKeyType.DateTime)
            {
                parseOk = DateTime.TryParse(valStr, System.Globalization.CultureInfo.InvariantCulture, out DateTime dt);
                if (parseOk) valNum = dt.Ticks;
            }
            else // String
            {
                int exactIdx = steps.FindIndex(s => s.RangeHiKey.Equals(valStr, StringComparison.OrdinalIgnoreCase));
                if (exactIdx >= 0)
                {
                    valNum = exactIdx;
                    parseOk = true;
                }
                else
                {
                    int insertIdx = 0;
                    for (int i = 0; i < steps.Count; i++)
                    {
                        if (string.Compare(valStr, steps[i].RangeHiKey, StringComparison.OrdinalIgnoreCase) > 0)
                        {
                            insertIdx = i + 1;
                        }
                    }
                    if (insertIdx == 0) valNum = -0.5;
                    else if (insertIdx >= steps.Count) valNum = steps.Count - 0.5;
                    else valNum = insertIdx - 0.5;
                    parseOk = true;
                }
            }

            if (!parseOk)
            {
                if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out valNum))
                {
                    parseOk = true;
                }
            }

            numericPosition = valNum;

            int matchStepIdx = -1;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].RangeHiKeyNumeric >= valNum)
                {
                    matchStepIdx = i;
                    break;
                }
            }

            if (matchStepIdx == -1)
            {
                var lastStep = steps.Last();
                estimatedRows = lastStep.AvgRangeRows;
                matchType = $"超出直方图上限，取最后区间 AVG_RANGE_ROWS";
            }
            else if (Math.Abs(steps[matchStepIdx].RangeHiKeyNumeric - valNum) < 1e-9 || (keyType == HistogramKeyType.String && valNum == matchStepIdx))
            {
                estimatedRows = steps[matchStepIdx].EqRows;
                matchType = "精确匹配 EQ_ROWS";
            }
            else if (matchStepIdx == 0)
            {
                estimatedRows = steps[0].AvgRangeRows;
                matchType = "低于直方图下限，取首个区间 AVG_RANGE_ROWS";
            }
            else
            {
                estimatedRows = steps[matchStepIdx].AvgRangeRows;
                matchType = $"落入区间, 取平均行数 AVG_RANGE_ROWS";
            }
        }
    }
}
