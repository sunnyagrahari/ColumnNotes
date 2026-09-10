using System.IO;
using System.Windows;
using System.Windows.Threading;
using ColumnNotes.Services;

namespace ColumnNotes;

public partial class App : Application
{
    void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUiCrash;
        AppDomain.CurrentDomain.UnhandledException += OnDomainCrash;
        try
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
        catch (Exception ex)
        {
            WriteCrash(ex);
            MessageBox.Show(
                "ColumnNotes could not start.\n\n" + ex.Message + "\n\nDetails saved to crash.log next to the app.",
                "ColumnNotes",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    void OnUiCrash(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        MessageBox.Show(e.Exception.Message, "ColumnNotes", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    void OnDomainCrash(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) WriteCrash(ex);
    }

    static void WriteCrash(Exception ex)
    {
        try
        {
            var dir = string.IsNullOrEmpty(AppPaths.DataDirectory) ? AppContext.BaseDirectory : AppPaths.DataDirectory;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "crash.log"), DateTime.Now.ToString("O") + "\n" + ex);
        }
        catch
        {
            /* ignore */
        }
    }
}
