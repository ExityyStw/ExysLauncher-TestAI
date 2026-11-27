using System;
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

    private void OnAccountMenuClicked(object? sender, RoutedEventArgs e)
    {
        RefreshAccountsMenu();
        if (sender is Button btn && btn.ContextMenu is ContextMenu menu)
        {
            menu.Open(btn);
        }
    }

    private void OnAccountCardContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        RefreshAccountsMenu();
    }

    private void RefreshAccountsMenu()
    {
        AccountsMenuItem.Items.Clear();
        RemoveAccountMenuItem.IsEnabled = DataContext is MainWindowViewModel { Accounts.Count: > 0 };

        if (DataContext is not MainWindowViewModel vm || vm.Accounts.Count == 0)
        {
            AccountsMenuItem.Items.Add(new MenuItem { Header = "Nenhuma conta", IsEnabled = false });
            return;
        }

        foreach (var account in vm.Accounts)
        {
            var isActive = vm.SelectedAccount?.Id == account.Id;
            var item = new MenuItem
            {
                Header = isActive ? $"{account.Username}  ✓" : account.Username,
                Command = vm.SelectAccountCommand,
                CommandParameter = account
            };
            AccountsMenuItem.Items.Add(item);
        }
    }

    private async void OnLoginMicrosoftClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        AccountProfile? account;
        try
        {
            var dialog = new MicrosoftLoginDialog();
            account = await dialog.ShowDialog<AccountProfile?>(this);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Erro no login Microsoft: {ex.Message}";
            return;
        }

        if (account == null)
        {
            vm.StatusMessage = "Login Microsoft cancelado ou não concluído.";
            return;
        }

        try
        {
            await vm.AccountManager.AddOrUpdateAccountAsync(account);
            vm.StatusMessage = $"Conta Microsoft '{account.Username}' adicionada.";
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Erro ao salvar conta: {ex.Message}";
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
            var accountId = vm.SelectedAccount.Id;
            var accountName = vm.SelectedAccount.Username;
            var confirm = await new ConfirmDialog("Remover Conta", $"Deseja remover a conta '{accountName}'?").ShowDialog<bool>(this);
            if (confirm == true)
            {
                await vm.AccountManager.RemoveAccountAsync(accountId);
                vm.StatusMessage = $"Conta '{accountName}' removida.";
            }
        }
    }

    private void OnShowSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SelectedTabIndex = 2;
    }

    private void OnNavHomeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedTabIndex = 0;
        }
    }

    private void OnNavInstancesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedTabIndex = 1;
        }
    }

    private void OnNavImportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedTabIndex = 3;
        }
    }

    private void OnHelpClicked(object? sender, RoutedEventArgs e)
    {
        var url = "https://github.com/ExityyStw/ExysLauncher-TestAI";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
