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
                LerpHex(230, 240, 250, 75, 142, 198, Math.Pow(t, 0.8)),
                LerpHex(205, 224, 242, 36, 99, 151, Math.Pow(t, 0.6)),
                LerpHex(155, 183, 207, 36, 99, 151, Math.Pow(t, 0.7)),
                1.0,
                GetBadgeBackgroundColorHex(activePercent),
                activePercent >= 15 ? "#FFFFFF" : "#172D40");
        }

        private static string GetBadgeBackgroundColorHex(double activePercent)
        {
            if (activePercent >= 40)
            {
                return "#246397";
            }

            if (activePercent >= 15)
            {
                return "#356FA1";
            }

            return "#CDE0F2";
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
