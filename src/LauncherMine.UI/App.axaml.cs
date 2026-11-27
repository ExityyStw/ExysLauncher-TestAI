using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LauncherMine.UI.Views;

namespace LauncherMine.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        InstallCrashHandlers();
    }

    private static void InstallCrashHandlers()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI thread", e.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Unobserved task", e.Exception);
            e.SetObserved();
        };
    }

    private static class CrashLog
    {
        public static void Write(string source, Exception exception)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LauncherMine", "logs");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File.WriteAllText(file,
                    $"[{DateTime.Now:O}] {source}\n{exception}\n");
            }
            catch
            {
                // Último recurso: falha silenciosa para não mascarar o erro original
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
