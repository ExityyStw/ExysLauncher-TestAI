using Avalonia.Controls;
using Avalonia.Threading;

namespace LauncherMine.UI.Views;

public partial class ConsoleWindow : Window
{
    public ConsoleWindow()
    {
        InitializeComponent();
    }

    public void AppendLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Limita o buffer do console para não crescer indefinidamente em sessões longas
            const int maxLength = 400_000;
            if (LogTextBox.Text is { Length: > maxLength } current)
            {
                LogTextBox.Text = current[(current.Length - maxLength / 2)..];
            }

            LogTextBox.Text += $"{message}\n";
            LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
        });
    }
}
