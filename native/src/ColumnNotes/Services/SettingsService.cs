using System.Text.Json;

namespace ColumnNotes.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "light";
    public int Zoom { get; set; } = 100;
    public bool ShowStatusBar { get; set; } = true;
    public bool WordWrap { get; set; } = true;
    public string FontFamily { get; set; } = "Calibri";
    public double FontSize { get; set; } = 16;
    public List<string> RecentFiles { get; set; } = new();
}

public static class SettingsService
{
    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath)) ?? new();
            }
        }
        catch { Current = new(); }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }

    public static void PushRecent(string path)
    {
        Current.RecentFiles = new[] { path }.Concat(Current.RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))).Take(10).ToList();
        Save();
    }
}
