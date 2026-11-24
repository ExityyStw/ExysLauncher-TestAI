using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LauncherMine.Core.Services;

namespace LauncherMine.UI.Views;

public sealed record CreateInstanceResult(string Name, string GameVersion, string LoaderType, string? LoaderVersion);

public partial class CreateInstanceDialog : Window
{
    public CreateInstanceDialog()
    {
        InitializeComponent();

        GameVersionCombo.ItemsSource = VersionCatalogService.DefaultVanillaReleases;
        GameVersionCombo.SelectedIndex = 0;

        LoaderTypeCombo.ItemsSource = new List<string> { "neoforge", "fabric", "forge", "vanilla" };
        LoaderTypeCombo.SelectedIndex = 0;

        UpdateLoaderVersions();

        LoaderTypeCombo.SelectionChanged += (_, _) => UpdateLoaderVersions();
    }

    private void UpdateLoaderVersions()
    {
        string selectedLoader = LoaderTypeCombo.SelectedItem?.ToString() ?? "neoforge";
        var versions = VersionCatalogService.GetVersionsForLoader(selectedLoader);
        LoaderVersionCombo.ItemsSource = versions;
        LoaderVersionCombo.SelectedIndex = 0;
        LoaderVersionCombo.IsEnabled = selectedLoader != "vanilla";
    }

    private void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        string name = InstanceNameBox.Text?.Trim() ?? "Nova Instancia";
        string gameVersion = GameVersionCombo.SelectedItem?.ToString() ?? "1.21.1";
        string loaderType = LoaderTypeCombo.SelectedItem?.ToString() ?? "neoforge";
        string? loaderVersion = LoaderVersionCombo.SelectedItem?.ToString();

        Close(new CreateInstanceResult(name, gameVersion, loaderType, loaderVersion));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
