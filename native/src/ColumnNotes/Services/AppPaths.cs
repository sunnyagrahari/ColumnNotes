using System.IO;

namespace ColumnNotes.Services;

public static class AppPaths
{
    public static bool IsPortable { get; private set; }
    public static string DataDirectory { get; private set; } = "";
    public static string ExeDirectory { get; private set; } = "";

    public static void Initialize(string[] args)
    {
        ExeDirectory = AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var portableFlag = System.IO.Path.Combine(ExeDirectory, "portable.txt");
        IsPortable = args.Any(a => string.Equals(a, "--portable", StringComparison.OrdinalIgnoreCase))
                     || File.Exists(portableFlag);
        DataDirectory = IsPortable
            ? ExeDirectory
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ColumnNotes");
        Directory.CreateDirectory(DataDirectory);
    }

    public static string SettingsPath => System.IO.Path.Combine(DataDirectory, "settings.json");
    public static string RecentPath => System.IO.Path.Combine(DataDirectory, "recent.json");
    public static string AutosavePath => System.IO.Path.Combine(DataDirectory, "autosave.cnotes");
}
