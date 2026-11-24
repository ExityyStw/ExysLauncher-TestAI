using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LauncherMine.UI.Views;

public partial class AddAccountDialog : Window
{
    public AddAccountDialog()
    {
        InitializeComponent();
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        string username = UsernameBox.Text?.Trim() ?? string.Empty;
        Close(username);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
