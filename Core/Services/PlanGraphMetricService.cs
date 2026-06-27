using System;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PlanGraphMetricService
    {
        public static string FormatNumber(double value)
        {
            if (value >= 1_000_000)
            {
                return (value / 1_000_000).ToString("0.0") + "M";
            }

            if (value >= 10_000)
            {
                return (value / 1000).ToString("0.0") + "K";
            }

            if (value >= 1000)
            {
                return (value / 1000).ToString("0") + "K";
            }

            return value.ToString("N0");
        }

        public static string FormatBytes(double bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
            {
                return (bytes / (1024 * 1024 * 1024)).ToString("0.0") + " GB";
            }

            if (bytes >= 1024 * 1024)
            {
                return (bytes / (1024 * 1024)).ToString("0.0") + " MB";
            }

            if (bytes >= 1024)
            {
                return (bytes / 1024).ToString("0.0") + " KB";
            }

            return bytes.ToString("0") + " B";
        }

        public static double CalculateLinkThickness(double metricValue)
        {
            if (metricValue <= 0)
            {
                return 1.5;
            }

            const double minWidth = 1.5;
            const double maxWidth = 12.0;
            const double alpha = 0.25;
            double logValue = Math.Log10(metricValue + 1);
            return minWidth + (maxWidth - minWidth) * Math.Tanh(alpha * logValue);
        }

        public static double CalculateLegacyConverterThickness(object? value)
        {
            double rows = ParseMetricValue(value);

            if (rows <= 0)
            {
                return 1.0;
            }

            double logValue = Math.Log10(rows) * 1.6;
            return Math.Max(1.0, Math.Min(14.0, logValue));
        }

        private static double ParseMetricValue(object? value)
        {
            return value switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                string s when NumericParser.TryParseInvariantDouble(s, out double parsed) => parsed,
                _ => 0
            };
        }
    }
}
