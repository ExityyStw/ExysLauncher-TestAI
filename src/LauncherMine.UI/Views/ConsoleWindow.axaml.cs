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
            LogTextBox.Text += $"{message}\n";
            LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
        });
    }
}
