namespace SqlXmlAnalyzer.Core.Services
{
    public enum PlanGraphRowSkewBrushKey
    {
        DimGray,
        DarkRed,
        DarkOrange,
        HealthyGreen
    }

    public sealed record PlanGraphRowSkewResult(
        PlanGraphRowSkewBrushKey BrushKey,
        string Warning);

    public sealed class PlanGraphRowSkewService
    {
        public PlanGraphRowSkewResult Analyze(double actualRows, double estimatedRows)
        {
            if (actualRows <= 0 || estimatedRows <= 0)
            {
                return new PlanGraphRowSkewResult(
                    PlanGraphRowSkewBrushKey.DimGray,
                    string.Empty);
            }

            double ratio = actualRows / estimatedRows;
            return new PlanGraphRowSkewResult(
                GetBrushKey(ratio),
                GetWarning(ratio));
        }

        private static PlanGraphRowSkewBrushKey GetBrushKey(double ratio)
        {
            if (ratio > 3.0 || ratio < 0.33)
            {
                return PlanGraphRowSkewBrushKey.DarkRed;
            }

            if (ratio > 1.5 || ratio < 0.7)
            {
                return PlanGraphRowSkewBrushKey.DarkOrange;
            }

            return PlanGraphRowSkewBrushKey.HealthyGreen;
        }

        private static string GetWarning(double ratio)
        {
            if (ratio > 5)
            {
                return "↑↑ 严重高估";
            }

            if (ratio > 2.5)
            {
                return "↑ 高估";
            }

            if (ratio < 0.2)
            {
                return "↓↓ 严重低估";
            }

            if (ratio < 0.5)
            {
                return "↓ 低估";
            }

            return string.Empty;
        }
    }
}
