using System;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record PlanGraphPage(int Start, int Count, int Total)
{
    public string Description => Total == 0 ? "未采集算子" : $"图中显示 {Start + 1}–{Start + Count} / 共 {Total} 个算子"
        + (Count < Total ? "；分段显示，可翻页或从诊断/指标表定位" : "");
}

public static class PlanGraphPageService
{
    public const int MaximumVisibleNodes = 64;
    public static PlanGraphPage ForIndex(int count, int index)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return new(0, 0, 0);
        int start = Math.Clamp(index, 0, count - 1) / MaximumVisibleNodes * MaximumVisibleNodes;
        return new(start, Math.Min(MaximumVisibleNodes, count - start), count);
    }
}
