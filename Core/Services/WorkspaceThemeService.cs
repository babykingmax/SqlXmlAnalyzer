using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Core.Services;

public static class WorkspaceThemeService
{
    public static IReadOnlyDictionary<string, Color> Palette(bool dark)
    {
        Color C(string light, string night) => (Color)ColorConverter.ConvertFromString(dark ? night : light);
        return new Dictionary<string, Color>
        {
            ["BackgroundBrush"] = C("#F3F3F3", "#181B20"),
            ["SurfaceBrush"] = C("#FFFFFF", "#23272E"),
            ["PlanTooltipSurfaceBrush"] = C("#F8FAFD", "#364150"),
            ["TextBrush"] = C("#202020", "#F2F4F8"),
            ["SecondaryTextBrush"] = C("#535B66", "#BCC5D1"),
            ["BorderBrush"] = C("#8A949F", "#8997A9"),
            ["AccentBrush"] = C("#005FB8", "#8CC5FF"),
            ["AccentHoverBrush"] = C("#00519E", "#AED6FF"),
            ["AccentPressedBrush"] = C("#004482", "#C4E2FF"),
            ["AccentForegroundBrush"] = C("#FFFFFF", "#122234"),
            ["SelectionBrush"] = C("#DCEBFA", "#31445C"),
            ["FocusBrush"] = C("#00519E", "#FFD369"),
            ["DisabledSurfaceBrush"] = C("#E2E5E9", "#353C46"),
            ["DisabledTextBrush"] = C("#56606C", "#B9C2CE"),
            ["WarningSurfaceBrush"] = C("#FFF4D0", "#3D3219"),
            ["WarningBrush"] = C("#784500", "#FFD578"),
            ["CriticalSurfaceBrush"] = C("#FCE8E8", "#442528"),
            ["CriticalBrush"] = C("#A51D2D", "#FFA9B2"),
            ["InfoBrush"] = C("#0759A5", "#9FCDFF"),
            ["GoodBrush"] = C("#24602C", "#A3DFAC"),
            ["AlertHighlightBrush"] = C("#A51D2D", "#FFA9B2"),
        };
    }

    public static void Apply(ResourceDictionary resources, bool dark)
    {
        foreach (var entry in Palette(dark))
        {
            var brush = new SolidColorBrush(entry.Value); brush.Freeze();
            resources[entry.Key] = brush;
        }
        resources["WorkspaceDarkTheme"] = dark;
        Logger.Debug($"IMP25 semantic theme applied: dark={dark}.");
    }
}
