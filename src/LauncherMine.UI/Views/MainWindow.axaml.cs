using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using LauncherMine.Core.Auth;
using LauncherMine.Core.Models;
using LauncherMine.UI.ViewModels;

namespace LauncherMine.UI.Views;

public partial class MainWindow : Window
{
    private ConsoleWindow? _consoleWindow;
    private readonly System.Collections.Generic.Queue<string> _pendingLogs = new();
    private const int MaxPendingLogs = 5000;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;

        vm.RequestOpenConsole += OpenConsole;

        vm.LogReceived += line =>
        {
            if (_consoleWindow != null)
            {
                _consoleWindow.AppendLog(line);
            }
            else
            {
                if (_pendingLogs.Count >= MaxPendingLogs)
                    _pendingLogs.Dequeue();
                _pendingLogs.Enqueue(line);
            }
        };

        Loaded += async (_, _) =>
        {
            await vm.InitializeAsync();
        };
    }

    private void OpenConsole()
    {
        if (_consoleWindow == null || !_consoleWindow.IsVisible)
        {
            _consoleWindow = new ConsoleWindow();
            _consoleWindow.Closed += (_, _) => _consoleWindow = null;
            _consoleWindow.Show();

            while (_pendingLogs.Count > 0)
            {
                _consoleWindow.AppendLog(_pendingLogs.Dequeue());
            }
        }
    }

    private void OnTabJogarClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SelectedTabIndex = 0;
    }

    private void OnTabInstanciasClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SelectedTabIndex = 1;
    }

    private void OnTabJvmClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SelectedTabIndex = 2;
    }

    private void OnTabImportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SelectedTabIndex = 3;
    }

    private async void OnCreateInstanceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var dialog = new CreateInstanceDialog();
        var result = await dialog.ShowDialog<CreateInstanceResult?>(this);

        if (result != null)
        {
            await vm.CreateNewInstanceAsync(result.Name, result.GameVersion, result.LoaderType, result.LoaderVersion);
        }
    }

    private void OnSelectInstanceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is InstanceMetadata inst && DataContext is MainWindowViewModel vm)
        {
            vm.SelectedInstance = inst;
            vm.SelectedTabIndex = 0;
            vm.StatusMessage = $"Instância '{inst.Name}' selecionada.";
        }
    }

    private async void OnBrowseZipClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar Modpack (.zip) do CurseForge ou Modrinth",
            AllowMultiple = false,
            FileTypeFilter = [
                new FilePickerFileType("Pacotes de Modpack (*.zip)")
                {
                    Patterns = ["*.zip"]
                }
            ]
        });

        var selected = files.FirstOrDefault();
        if (selected != null)
        {
            vm.ImportZipPath = selected.Path.LocalPath;
        }
    }

    private async void OnConfirmImportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && !string.IsNullOrWhiteSpace(vm.ImportZipPath))
        {
            await vm.ImportZipModpackAsync(vm.ImportZipPath);
        }
    }

    private void OnAddAccountMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is ContextMenu menu)
        {
            menu.Open(btn);
        }
    }

    private async void OnLoginMicrosoftClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new MicrosoftLoginDialog();
        var account = await dialog.StartLoginAsync();

        if (account != null && DataContext is MainWindowViewModel vm)
        {
            await vm.AccountManager.AddOrUpdateAccountAsync(account);
            vm.StatusMessage = $"Conta Microsoft '{account.Username}' adicionada.";
        }
    }

    private async void OnSelectJavaClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar binário Java (java.exe ou java)",
            AllowMultiple = false,
            FileTypeFilter = [
                new FilePickerFileType("Java Executable")
                {
                    Patterns = ["java", "java.exe"]
                }
            ]
        });

        var selected = files.FirstOrDefault();
        if (selected?.Path.LocalPath is { Length: > 0 } path)
        {
            vm.CustomJavaPath = path;
            vm.StatusMessage = $"Java customizado definido: {Path.GetFileName(path)}";
        }
    }

    private async void OnAddAccountClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new AddAccountDialog();
        var result = await dialog.ShowDialog<string?>(this);

        if (!string.IsNullOrWhiteSpace(result) && DataContext is MainWindowViewModel vm)
        {
            await vm.AddOfflineAccountAsync(result);
        }
    }

    private async void OnRemoveAccountClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedAccount != null)
        {
            var confirm = await new ConfirmDialog("Remover Conta", $"Deseja remover a conta '{vm.SelectedAccount.Username}'?").ShowDialog<bool>(this);
            if (confirm == true)
            {
                await vm.AccountManager.RemoveAccountAsync(vm.SelectedAccount.Id);
                vm.StatusMessage = $"Conta '{vm.SelectedAccount.Username}' removida.";
            }
        }
    }

    private void OnThemeToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is Avalonia.Application app)
        {
            var currentTheme = app.RequestedThemeVariant;
            app.RequestedThemeVariant = currentTheme == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    private void OnShowInstancesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SelectedTabIndex = 1;
    }

    private void OnShowSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SelectedTabIndex = 2;
    }
}
