using System.Globalization;

namespace SqlXmlAnalyzer.Core
{
    public static class NumericParser
    {
        /// <summary>
        /// Culture-invariant double parser to handle different regional settings (e.g., German/French comma decimals).
        /// </summary>
        public static bool TryParseInvariantDouble(string? val, out double result)
        {
            result = 0.0;
            if (string.IsNullOrEmpty(val)) return false;
            return double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>
        /// Culture-invariant double parsing helper returning default value (0.0) if parsing fails.
        /// </summary>
        public static double ParseInvariantDouble(string? val, double defaultValue = 0.0)
        {
            if (TryParseInvariantDouble(val, out double result)) return result;
            return defaultValue;
        }
    }
}
