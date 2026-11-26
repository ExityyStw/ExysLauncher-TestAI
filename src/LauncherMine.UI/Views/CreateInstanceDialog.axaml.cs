using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LauncherMine.Core.Services;

namespace LauncherMine.UI.Views;

public sealed record CreateInstanceResult(string Name, string GameVersion, string LoaderType, string? LoaderVersion);

public partial class CreateInstanceDialog : Window
{
    private CancellationTokenSource? _loaderCts;
    private bool _userEditedName;

    public CreateInstanceDialog()
    {
        InitializeComponent();

        GameVersionCombo.ItemsSource = VersionCatalogService.CachedVanillaReleases;
        GameVersionCombo.SelectedIndex = 0;

        LoaderTypeCombo.ItemsSource = new List<string> { "neoforge", "fabric", "forge", "vanilla" };
        LoaderTypeCombo.SelectedIndex = 0;

        InstanceNameBox.TextChanged += (_, _) =>
        {
            if (InstanceNameBox.IsFocused)
                _userEditedName = true;
        };

        UpdateLoaderVersionsAsync();
        LoadVanillaVersionsAsync();

        GameVersionCombo.SelectionChanged += (_, _) =>
        {
            UpdateSuggestedName();
            UpdateLoaderVersionsAsync();
        };

        LoaderTypeCombo.SelectionChanged += (_, _) =>
        {
            UpdateSuggestedName();
            UpdateLoaderVersionsAsync();
        };
    }

    private void UpdateSuggestedName()
    {
        if (_userEditedName) return;
        string gameVersion = GameVersionCombo.SelectedItem?.ToString() ?? "1.21.1";
        string loaderType = LoaderTypeCombo.SelectedItem?.ToString() ?? "neoforge";
        string label = loaderType switch
        {
            "neoforge" => "NeoForge",
            "fabric" => "Fabric",
            "forge" => "Forge",
            _ => "Vanilla"
        };
        InstanceNameBox.Text = $"{label} {gameVersion}";
    }

    private async void LoadVanillaVersionsAsync()
    {
        var versions = await VersionCatalogService.GetVanillaVersionsAsync();
        Dispatcher.UIThread.Post(() =>
        {
            string? selected = GameVersionCombo.SelectedItem?.ToString();
            GameVersionCombo.ItemsSource = versions;
            if (!string.IsNullOrEmpty(selected) && versions.Contains(selected))
                GameVersionCombo.SelectedItem = selected;
            else
                GameVersionCombo.SelectedIndex = 0;
        });
    }

    private async void UpdateLoaderVersionsAsync()
    {
        _loaderCts?.Cancel();
        _loaderCts = new CancellationTokenSource();
        var token = _loaderCts.Token;

        string selectedLoader = LoaderTypeCombo.SelectedItem?.ToString() ?? "neoforge";
        string gameVersion = GameVersionCombo.SelectedItem?.ToString() ?? "1.21.1";

        var syncFallback = VersionCatalogService.GetVersionsForLoader(selectedLoader);
        LoaderVersionCombo.ItemsSource = syncFallback;
        LoaderVersionCombo.SelectedIndex = 0;
        LoaderVersionCombo.IsEnabled = selectedLoader != "vanilla";

        if (selectedLoader == "vanilla")
            return;

        try
        {
            var versions = await VersionCatalogService.GetVersionsForLoaderAsync(selectedLoader, gameVersion, token);
            if (token.IsCancellationRequested) return;

            Dispatcher.UIThread.Post(() =>
            {
                LoaderVersionCombo.ItemsSource = versions;
                LoaderVersionCombo.SelectedIndex = 0;
            });
        }
        catch
        {
            // Mantém fallback offline
        }
    }

    private void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        string name = InstanceNameBox.Text?.Trim() ?? "Nova Instancia";
        if (string.IsNullOrWhiteSpace(name))
            name = "Nova Instancia";

        string gameVersion = GameVersionCombo.SelectedItem?.ToString() ?? "1.21.1";
        string loaderType = LoaderTypeCombo.SelectedItem?.ToString() ?? "neoforge";
        string? loaderVersion = loaderType == "vanilla" ? null : LoaderVersionCombo.SelectedItem?.ToString();

        Close(new CreateInstanceResult(name, gameVersion, loaderType, loaderVersion));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
