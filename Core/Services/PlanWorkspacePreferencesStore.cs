using System.IO;
using System.Text.Json;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record PlanGridColumnPreference(string Key, double Width, int DisplayIndex, bool Visible);

/// <summary>Presentation preferences only; never contains a plan, SQL, object names or file paths.</summary>
public sealed record PlanWorkspacePreferences
{
    public int Version { get; init; } = 1;
    public double LeftWidth { get; init; } = 280;
    public double RightWidth { get; init; } = 320;
    public bool LeftOpen { get; init; } = true;
    public bool RightOpen { get; init; } = true;
    public bool AuxiliaryOpen { get; init; }
    public double AuxiliaryHeight { get; init; } = 220;
    public string ActiveView { get; init; } = "graph";
    public string CompactPane { get; init; } = "graph";
    public IReadOnlyList<PlanGridColumnPreference> Columns { get; init; } = Array.Empty<PlanGridColumnPreference>();
    public PlanGraphPreferences Graph { get; init; } = new();
}

public sealed class PlanWorkspacePreferencesStore
{
    private const int MaximumBytes = 64 * 1024;
    private readonly string _path;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static PlanWorkspacePreferencesStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlXmlAnalyzer", "plan-workspace.json"));

    public PlanWorkspacePreferencesStore(string path) => _path = Path.GetFullPath(path);

    public PlanWorkspacePreferences Load()
    {
        lock (_gate)
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > MaximumBytes) return new();
                return Normalize(JsonSerializer.Deserialize<PlanWorkspacePreferences>(stream, JsonOptions));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Invalid or unavailable preferences must not prevent opening a plan.
                return new();
            }
        }
    }

    public bool Save(PlanWorkspacePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (_gate)
        {
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(Normalize(preferences), JsonOptions);
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                File.Move(temporary, _path, overwrite: true);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Logger.Warning("执行计划布局偏好未能保存；当前布局仍然可用。");
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { Logger.Warning("执行计划布局临时文件清理失败。"); }
            }
        }
    }

    public static PlanWorkspacePreferences Normalize(PlanWorkspacePreferences? preferences)
    {
        if (preferences == null || preferences.Version != 1) return new();
        static double Bound(double value, double min, double max, double fallback) =>
            double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        var columns = (preferences.Columns ?? Array.Empty<PlanGridColumnPreference>())
            .Where(column => column != null && !string.IsNullOrWhiteSpace(column.Key) && column.Key.Length <= 80)
            .DistinctBy(column => column.Key, StringComparer.Ordinal).Take(40)
            .Select(column => column with { Width = Bound(column.Width, 48, 800, 120), DisplayIndex = Math.Clamp(column.DisplayIndex, 0, 39) })
            .ToArray();
        return preferences with
        {
            LeftWidth = Bound(preferences.LeftWidth, 220, 480, 280),
            RightWidth = Bound(preferences.RightWidth, 260, 520, 320),
            AuxiliaryHeight = Bound(preferences.AuxiliaryHeight, 120, 600, 220),
            ActiveView = preferences.ActiveView == "operators" ? "operators" : "graph",
            CompactPane = preferences.CompactPane is "issues" or "details" ? preferences.CompactPane : "graph",
            Columns = columns,
            Graph = preferences.Graph ?? new()
        };
    }
}
