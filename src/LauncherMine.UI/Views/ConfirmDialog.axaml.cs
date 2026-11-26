using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LauncherMine.UI.Views;

/// <summary>
/// Diálogo modal genérico de confirmação Sim/Não.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this("Tem certeza?") { }

    public ConfirmDialog(string message, string confirmText = "Confirmar")
    {
        InitializeComponent();
        MessageText.Text = message;
        YesButton.Content = confirmText;
    }

    private void OnYesClicked(object? sender, RoutedEventArgs e) => Close(true);
    private void OnNoClicked(object? sender, RoutedEventArgs e) => Close(false);
}
