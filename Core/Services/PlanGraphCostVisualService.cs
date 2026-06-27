using System;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphCostVisualStyle(
        string BackgroundTopColorHex,
        string BackgroundBottomColorHex,
        string BorderColorHex,
        double BorderThickness,
        string BadgeBackgroundColorHex,
        string BadgeForegroundColorHex);

    public sealed class PlanGraphCostVisualService
    {
        public PlanGraphCostVisualStyle GetStyle(double activePercent)
        {
            double t = Math.Clamp(activePercent, 0, 100) / 100.0;

            return new PlanGraphCostVisualStyle(
                LerpHex(255, 255, 255, 255, 230, 230, Math.Pow(t, 0.8)),
                LerpHex(245, 247, 250, 255, 190, 190, Math.Pow(t, 0.6)),
                LerpHex(176, 190, 197, 211, 47, 47, Math.Pow(t, 0.7)),
                activePercent >= 30 ? 2.0 : 1.0,
                GetBadgeBackgroundColorHex(activePercent),
                activePercent >= 15 ? "#FFFFFF" : "#000000");
        }

        private static string GetBadgeBackgroundColorHex(double activePercent)
        {
            if (activePercent >= 40)
            {
                return "#EF5350";
            }

            if (activePercent >= 15)
            {
                return "#FFB300";
            }

            return "#CFD8DC";
        }

        private static string LerpHex(
            byte startR,
            byte startG,
            byte startB,
            byte endR,
            byte endG,
            byte endB,
            double weight)
        {
            weight = Math.Max(0, Math.Min(1, weight));
            byte r = (byte)(startR + (endR - startR) * weight);
            byte g = (byte)(startG + (endG - startG) * weight);
            byte b = (byte)(startB + (endB - startB) * weight);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}
