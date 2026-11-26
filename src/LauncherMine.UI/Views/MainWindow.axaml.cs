using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LauncherMine.Core.Models;
using LauncherMine.UI.ViewModels;

namespace LauncherMine.UI.Views;

public partial class MainWindow : Window
{
    private ConsoleWindow? _consoleWindow;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;

        vm.RequestOpenConsole += () =>
        {
            if (_consoleWindow == null || !_consoleWindow.IsVisible)
            {
                _consoleWindow = new ConsoleWindow();
                _consoleWindow.Closed += (_, _) => _consoleWindow = null;
                _consoleWindow.Show();
            }
        };

        vm.LogReceived += line =>
        {
            _consoleWindow?.AppendLog(line);
        };

        Loaded += async (_, _) =>
        {
            await vm.InitializeAsync();
        };
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

    private void OnOpenInstanceFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is InstanceMetadata inst && DataContext is MainWindowViewModel vm)
        {
            vm.OpenInstanceFolder(inst);
        }
    }

    private async void OnDeleteInstanceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is InstanceMetadata inst && DataContext is MainWindowViewModel vm)
        {
            await vm.DeleteInstanceAsync(inst);
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
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.ImportZipModpackAsync();
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
}
