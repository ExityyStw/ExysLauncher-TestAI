using Avalonia;
using System;

namespace LauncherMine.UI;

internal static class Program
{
    // Ponto de entrada compatível com inicialização nativa rápida e compilação AOT
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
