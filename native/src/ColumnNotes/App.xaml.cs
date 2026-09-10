using System.Windows;
using ColumnNotes.Services;

namespace ColumnNotes;

public partial class App : Application
{
    void OnStartup(object sender, StartupEventArgs e)
    {
        AppPaths.Initialize(e.Args);
        SettingsService.Load();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        foreach (var arg in e.Args)
        {
            if (arg.StartsWith('-')) continue;
            if (File.Exists(arg))
            {
                window.OpenPath(arg);
                break;
            }
        }
    }
}
