namespace ColumnNotes.Services;

public static class AppPaths
{
    public static bool IsPortable { get; private set; }
    public static string DataDirectory { get; private set; } = "";
    public static string ExeDirectory { get; private set; } = "";

    public static void Initialize(string[] args)
    {
        ExeDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var portableFlag = Path.Combine(ExeDirectory, "portable.txt");
        IsPortable = args.Any(a => string.Equals(a, "--portable", StringComparison.OrdinalIgnoreCase))
                     || File.Exists(portableFlag);
        DataDirectory = IsPortable
            ? ExeDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ColumnNotes");
        Directory.CreateDirectory(DataDirectory);
    }

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string RecentPath => Path.Combine(DataDirectory, "recent.json");
    public static string AutosavePath => Path.Combine(DataDirectory, "autosave.cnotes");
}
