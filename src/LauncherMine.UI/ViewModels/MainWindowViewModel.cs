using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LauncherMine.Core.Auth;
using LauncherMine.Core.Launch;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;
using LauncherMine.Core.Services;
using LauncherMine.Core.Updater;
using LauncherMine.Core.Utils;

namespace LauncherMine.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISecureVault _secureVault;
    private readonly AccountManager _accountManager;
    private readonly InstanceManager _instanceManager;
    private readonly MinecraftLauncher _minecraftLauncher;
    private readonly ModpackUpdater _updater;
    private readonly ModpackImporter _importer;
    private readonly ManifestService _manifestService;
    private bool _isSyncingInstance;
    private CancellationTokenSource? _loaderFetchCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayTab), nameof(IsInstancesTab), nameof(IsJvmTab), nameof(IsImportTab))]
    private int _selectedTabIndex = 0; // 0 = Jogar, 1 = Instâncias, 2 = Loader & JVM, 3 = Importar ZIP

    public bool IsPlayTab => SelectedTabIndex == 0;
    public bool IsInstancesTab => SelectedTabIndex == 1;
    public bool IsJvmTab => SelectedTabIndex == 2;
    public bool IsImportTab => SelectedTabIndex == 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeToggleIcon), nameof(ThemeToggleTooltip))]
    private bool _isDarkMode = false;

    public string ThemeToggleIcon => IsDarkMode ? "☀" : "🌙";
    public string ThemeToggleTooltip => IsDarkMode ? "Voltar ao Tema Papel (Claro)" : "Ativar Modo Escuro";

    [ObservableProperty]
    private string _statusMessage = "Pronto para jogar";

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private bool _isProgressVisible = false;

    [ObservableProperty]
    private bool _isBusy = false;

    // Detecção Automática da Memória RAM do Computador
    [ObservableProperty]
    private int _totalSystemRamMb = 8192;

    [ObservableProperty]
    private int _maxRamSliderMb = 6144;

    [ObservableProperty]
    private int _minRamSliderMb = 1024;

    [ObservableProperty]
    private int _ramAllocationMb = 4096;

    [ObservableProperty]
    private string _ramInfoText = "Detectando hardware...";

    [ObservableProperty]
    private bool _hasRecommended16GbRam = false;

    [ObservableProperty]
    private string _ramBadgeText = "Validando RAM...";

    [ObservableProperty]
    private bool _closeLauncherOnLaunch = false;

    [ObservableProperty]
    private bool _openConsoleOnLaunch = false;

    [ObservableProperty]
    private string _customJvmArgsText = string.Empty;

    [ObservableProperty]
    private string _instanceManifestUrl = string.Empty;

    [ObservableProperty]
    private string _selectedJavaDisplay = "Detectando Java...";

    [ObservableProperty]
    private string _customJavaPath = string.Empty;

    public ObservableCollection<string> JvmPresetNames { get; } = [];

    [ObservableProperty]
    private string _selectedJvmPreset = "G1GC Ultra-Otimizado (Modpacks 300+ Mods • Recomendado)";

    public ObservableCollection<string> AvailableLoaders { get; } = ["neoforge", "fabric", "forge", "vanilla"];

    [ObservableProperty]
    private string _selectedLoader = "neoforge";

    public ObservableCollection<string> AvailableLoaderVersions { get; } = [];

    [ObservableProperty]
    private string _selectedLoaderVersion = "21.1.84";

    public ObservableCollection<string> AvailableGameVersions { get; } = [];

    [ObservableProperty]
    private string _selectedGameVersion = "1.21.1";

    public ObservableCollection<AccountProfile> Accounts { get; } = [];

    [ObservableProperty]
    private bool _hasAccounts;

    [ObservableProperty]
    private string _statusBadgeText = "Pronto para jogar";

    [ObservableProperty]
    private AccountProfile? _selectedAccount;

    public ObservableCollection<InstanceMetadata> Instances { get; } = [];

    [ObservableProperty]
    private InstanceMetadata? _selectedInstance;

    [ObservableProperty]
    private string _importZipPath = string.Empty;

    public event Action<string>? LogReceived;
    public event Action? RequestOpenConsole;

    public AccountManager AccountManager => _accountManager;

    public MainWindowViewModel()
    {
        _secureVault = SecureVaultFactory.Create();
        _accountManager = new AccountManager(_secureVault);
        _instanceManager = new InstanceManager();
        _minecraftLauncher = new MinecraftLauncher(_instanceManager, _accountManager);
        _updater = new ModpackUpdater(maxConcurrentDownloads: 6);
        _importer = new ModpackImporter(_instanceManager);
        _manifestService = new ManifestService();

        _minecraftLauncher.GameLogReceived += line => LogReceived?.Invoke(line);

        _accountManager.AccountsChanged += SyncAccountsFromManager;
        _accountManager.ActiveAccountChanged += acc => SelectedAccount = acc;

        // 1. Detecta Memória RAM Real do PC
        DetectSystemRam();

        // 2. Preenche Presets de GC
        foreach (var key in JvmPresets.Presets.Keys)
        {
            JvmPresetNames.Add(key);
        }
        UpdateJvmArgsFromPreset();

        // 3. Preenche Versões Iniciais
        foreach (var v in VersionCatalogService.DefaultVanillaReleases)
        {
            AvailableGameVersions.Add(v);
        }
        UpdateLoaderVersionsListSync();

        // 4. Carrega preferência de tema (padrão: Tema Papel Claro)
        LoadThemePreference();
    }

    private static string GetUiSettingsPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "LauncherMine", "ui-settings.json");
    }

    private void LoadThemePreference()
    {
        try
        {
            string path = GetUiSettingsPath();
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("isDarkMode", out var prop) && prop.ValueKind == JsonValueKind.True)
                {
                    IsDarkMode = true;
                }
            }
        }
        catch
        {
            // Ignora erro de leitura e mantém Tema Papel padrão
        }

        ApplyTheme(IsDarkMode);
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        ApplyTheme(value);
        try
        {
            string path = GetUiSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { isDarkMode = value }));
        }
        catch
        {
            // Ignora erro de escrita de preferência visual
        }
    }

    private static void ApplyTheme(bool dark)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
    }

    private void DetectSystemRam()
    {
        TotalSystemRamMb = SystemInfoHelper.GetTotalPhysicalRamMb();
        MaxRamSliderMb = SystemInfoHelper.GetSafeMaxRamMb(TotalSystemRamMb);
        MinRamSliderMb = 1024;

        double totalGb = Math.Round(TotalSystemRamMb / 1024.0, 1);
        double maxGb = Math.Round(MaxRamSliderMb / 1024.0, 1);

        // Valida se o PC atende à recomendação de 16 GB para modpacks pesados (tolera ~15.5 GB reais reportados pelo Windows)
        HasRecommended16GbRam = TotalSystemRamMb >= 15360;

        if (HasRecommended16GbRam)
        {
            RamAllocationMb = Math.Min(6144, MaxRamSliderMb);
            RamBadgeText = $"✓ {totalGb:0.#} GB RAM (16 GB+ OK)";
            RamInfoText = $"Hardware validado: {totalGb:0.#} GB de RAM física. Limite seguro: até {maxGb:0.#} GB ({MaxRamSliderMb} MB).";
        }
        else if (TotalSystemRamMb >= 8192)
        {
            RamAllocationMb = Math.Min(4096, MaxRamSliderMb);
            RamBadgeText = $"⚠ {totalGb:0.#} GB RAM (< 16 GB)";
            RamInfoText = $"PC com {totalGb:0.#} GB de RAM (abaixo dos 16 GB ideais para 300+ mods). Limite seguro: {maxGb:0.#} GB.";
        }
        else
        {
            RamAllocationMb = Math.Min(2048, MaxRamSliderMb);
            RamBadgeText = $"⚠ {totalGb:0.#} GB RAM (Baixa)";
            RamInfoText = $"Atenção: apenas {totalGb:0.#} GB de RAM detectados. Recomendado Vanilla ou packs leves. Limite: {maxGb:0.#} GB.";
        }
    }

    public async Task InitializeAsync()
    {
        await _accountManager.LoadAccountsAsync();
        SyncAccountsFromManager();

        await _instanceManager.LoadInstancesAsync();
        SyncInstancesFromManager();

        if (Instances.Count > 0)
        {
            SelectedInstance = Instances[0];
        }
        else
        {
            SelectedInstance = null;
        }

        // Busca versões online (Vanilla + Loaders) em segundo plano sem travar a interface
        _ = Task.Run(async () =>
        {
            var onlineVersions = await VersionCatalogService.GetVanillaVersionsAsync();
            var loaderVersions = await VersionCatalogService.GetVersionsForLoaderAsync(SelectedLoader, SelectedGameVersion);

            Dispatcher.UIThread.Post(() =>
            {
                string currentGameVer = SelectedGameVersion;
                AvailableGameVersions.Clear();
                foreach (var v in onlineVersions)
                    AvailableGameVersions.Add(v);

                if (!AvailableGameVersions.Contains(currentGameVer) && !string.IsNullOrWhiteSpace(currentGameVer))
                    AvailableGameVersions.Insert(0, currentGameVer);

                SelectedGameVersion = currentGameVer;
                ApplyLoaderVersionsList(loaderVersions, preserveSelected: SelectedLoaderVersion);
            });
        });
    }

    private void SyncAccountsFromManager()
    {
        Accounts.Clear();
        foreach (var acc in _accountManager.Accounts)
        {
            Accounts.Add(acc);
        }
        SelectedAccount = _accountManager.ActiveAccount;
        HasAccounts = Accounts.Count > 0;
    }

    public void SyncInstancesFromManager()
    {
        Instances.Clear();
        foreach (var inst in _instanceManager.Instances)
        {
            Instances.Add(inst);
        }
    }

    partial void OnSelectedInstanceChanged(InstanceMetadata? value)
    {
        StatusBadgeText = value != null ? "Em jogo" : "Pronto para jogar";

        if (value == null) return;

        _isSyncingInstance = true;
        try
        {
            RamAllocationMb = Math.Clamp(value.MaxMemoryMb, MinRamSliderMb, MaxRamSliderMb);
            CloseLauncherOnLaunch = value.CloseLauncherOnLaunch;

            if (!string.IsNullOrWhiteSpace(value.GameVersion))
            {
                if (!AvailableGameVersions.Contains(value.GameVersion))
                    AvailableGameVersions.Insert(0, value.GameVersion);
                SelectedGameVersion = value.GameVersion;
            }

            SelectedLoader = string.IsNullOrWhiteSpace(value.LoaderType) ? "vanilla" : value.LoaderType;

            if (value.ExtraJvmArgs is { Count: > 0 })
                CustomJvmArgsText = string.Join(" ", value.ExtraJvmArgs);

            InstanceManifestUrl = value.ManifestUrl ?? string.Empty;

            UpdateLoaderVersionsListSync(preserveSelected: value.LoaderVersion);
            RefreshJavaDisplay();
        }
        finally
        {
            _isSyncingInstance = false;
        }

        _ = RefreshLoaderVersionsOnlineAsync(SelectedLoader, SelectedGameVersion, value.LoaderVersion);
    }

    [RelayCommand]
    public async Task PlayAsync()
    {
        if (SelectedAccount == null || SelectedInstance == null || IsBusy) return;

        IsBusy = true;
        IsProgressVisible = true;
        ProgressValue = 0;

        try
        {
            SelectedInstance.MaxMemoryMb = RamAllocationMb;
            SelectedInstance.CloseLauncherOnLaunch = CloseLauncherOnLaunch;
            SelectedInstance.GameVersion = SelectedGameVersion;
            SelectedInstance.LoaderType = SelectedLoader;
            SelectedInstance.LoaderVersion = SelectedLoader == "vanilla" ? null : SelectedLoaderVersion;
            SelectedInstance.ManifestUrl = string.IsNullOrWhiteSpace(InstanceManifestUrl) ? null : InstanceManifestUrl.Trim();

            if (!string.IsNullOrWhiteSpace(CustomJvmArgsText))
            {
                SelectedInstance.ExtraJvmArgs = [.. CustomJvmArgsText.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
            }

            await _instanceManager.SaveInstanceMetadataAsync(SelectedInstance);

            // 1. Sincronização Prévia do Modpack
            // Prioridade: manifesto remoto HTTPS configurado na instância.
            // Fallback: sample-manifest.json local apenas para desenvolvimento.
            string mcDir = _instanceManager.GetInstanceMinecraftDirectory(SelectedInstance.Id);
            string? remoteManifestUrl = SelectedInstance.ManifestUrl;

            if (!string.IsNullOrWhiteSpace(remoteManifestUrl))
            {
                StatusMessage = "Baixando manifesto remoto do modpack...";
                var manifest = await _manifestService.FetchManifestAsync(remoteManifestUrl);

                // Alerta de divergência entre manifesto e configuração da instância
                if (!string.Equals(manifest.GameVersion, SelectedInstance.GameVersion, StringComparison.OrdinalIgnoreCase))
                {
                    LogReceived?.Invoke($"[AVISO] Manifesto declara Minecraft {manifest.GameVersion}, instância está em {SelectedInstance.GameVersion}.");
                }
                var manifestLoader = manifest.Loader.Type?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(manifestLoader) && manifestLoader != "vanilla" &&
                    !string.Equals(manifestLoader, SelectedInstance.LoaderType, StringComparison.OrdinalIgnoreCase))
                {
                    LogReceived?.Invoke($"[AVISO] Manifesto declara loader '{manifest.Loader.Type}', instância usa '{SelectedInstance.LoaderType}'.");
                }

                StatusMessage = "Sincronizando modpack (Validação SHA-256)...";
                var syncProgress = new Progress<UpdateProgress>(report =>
                {
                    ProgressValue = report.ProgressPercentage;
                    StatusMessage = $"[Sync] {report.ProcessedFiles}/{report.TotalFiles} arquivos • {report.CurrentFileName}";
                });

                // Falha aqui ABORTA o launch: manifesto configurado e inválido = não abrir o jogo
                await _updater.SynchronizeAsync(mcDir, manifest, syncProgress);
                StatusMessage = $"Modpack '{manifest.ModpackName}' v{manifest.Version} sincronizado.";
            }
            else
            {
                // Fallback de desenvolvimento: manifesto local ao lado do executável
                string localManifestPath = Path.Combine(AppContext.BaseDirectory, "sample-manifest.json");
                if (File.Exists(localManifestPath))
                {
                    try
                    {
                        string manifestJson = await File.ReadAllTextAsync(localManifestPath);
                        var manifest = JsonSerializer.Deserialize<ModpackManifest>(manifestJson);

                        if (manifest != null && manifest.Files != null && manifest.Files.Count > 0)
                        {
                            StatusMessage = "Sincronizando modpack local (Validação SHA-256)...";
                            var syncProgress = new Progress<UpdateProgress>(report =>
                            {
                                ProgressValue = report.ProgressPercentage;
                                StatusMessage = $"[Sync] {report.ProcessedFiles}/{report.TotalFiles} arquivos • {report.CurrentFileName}";
                            });

                            await _updater.SynchronizeAsync(mcDir, manifest, syncProgress);
                        }
                    }
                    catch (Exception syncEx)
                    {
                        LogReceived?.Invoke($"[AVISO] Falha ao verificar manifesto local: {syncEx.Message}");
                    }
                }
            }

            // 2. Inicialização do Minecraft
            StatusMessage = "Iniciando Minecraft...";
            var launchProgress = new Progress<LaunchProgress>(lp =>
            {
                ProgressValue = lp.Percentage;
                StatusMessage = lp.StepDescription;
            });

            if (OpenConsoleOnLaunch)
            {
                RequestOpenConsole?.Invoke();
            }

            await _minecraftLauncher.LaunchAsync(SelectedInstance, launchProgress);

            if (!CloseLauncherOnLaunch)
            {
                StatusMessage = "Jogo iniciado com sucesso!";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            LogReceived?.Invoke($"[ERRO AO JOGAR] {ex}");
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }

    [RelayCommand]
    public void OpenInstanceFolder(InstanceMetadata? instance)
    {
        var target = instance ?? SelectedInstance;
        if (target != null)
        {
            _instanceManager.OpenInstanceFolder(target.Id);
        }
    }

    [RelayCommand]
    public async Task DeleteInstanceAsync(InstanceMetadata? instance)
    {
        var target = instance ?? SelectedInstance;
        if (target != null)
        {
            await _instanceManager.DeleteInstanceAsync(target.Id);
            SyncInstancesFromManager();
            SelectedInstance = Instances.FirstOrDefault();
            StatusMessage = $"Instância '{target.Name}' excluída.";
        }
    }

    public async Task CreateNewInstanceAsync(string name, string gameVersion, string loaderType, string? loaderVersion)
    {
        var newInst = await _instanceManager.CreateInstanceAsync(name, gameVersion, loaderType, loaderVersion);
        SyncInstancesFromManager();
        SelectedInstance = newInst;
        SelectedTabIndex = 0;
        StatusMessage = $"Instância '{name}' criada com sucesso!";
    }

    [RelayCommand]
    public async Task ImportZipModpackAsync(string? explicitPath = null)
    {
        string path = explicitPath ?? ImportZipPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = "Arquivo .zip inválido ou não encontrado.";
            return;
        }

        IsBusy = true;
        IsProgressVisible = true;
        ProgressValue = 10;

        try
        {
            var report = new Progress<string>(msg =>
            {
                StatusMessage = msg;
            });

            var newInstance = await _importer.ImportFromZipAsync(path, report);

            await _instanceManager.LoadInstancesAsync();
            SyncInstancesFromManager();
            SelectedInstance = Instances.FirstOrDefault(i => i.Id == newInstance.Id) ?? newInstance;
            SelectedTabIndex = 0;
            StatusMessage = $"Modpack '{newInstance.Name}' importado com sucesso!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao importar: {ex.Message}";
            LogReceived?.Invoke($"[ERRO DE IMPORTAÇÃO] {ex}");
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }

    [RelayCommand]
    public async Task AddOfflineAccountAsync(string username)
    {
        try
        {
            var account = OfflineAuthService.CreateOfflineAccount(username);
            await _accountManager.AddOrUpdateAccountAsync(account);
            StatusMessage = $"Conta offline '{account.Username}' adicionada.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Conta inválida: {ex.Message}";
            LogReceived?.Invoke($"[ERRO CONTA] {ex.Message}");
        }
    }

    [RelayCommand]
    private void SelectAccount(AccountProfile? account)
    {
        if (account != null)
        {
            SelectedAccount = account;
        }
    }

    [RelayCommand]
    public async Task RemoveAccountAsync(AccountProfile? account = null)
    {
        var target = account ?? SelectedAccount;
        if (target != null)
        {
            await _accountManager.RemoveAccountAsync(target.Id);
            StatusMessage = $"Conta '{target.Username}' removida.";
        }
    }

    partial void OnSelectedLoaderChanged(string value)
    {
        if (_isSyncingInstance) return;
        UpdateLoaderVersionsListSync();
        _ = RefreshLoaderVersionsOnlineAsync(value, SelectedGameVersion, null);
    }

    partial void OnSelectedGameVersionChanged(string value)
    {
        if (_isSyncingInstance || string.IsNullOrWhiteSpace(value)) return;
        _ = RefreshLoaderVersionsOnlineAsync(SelectedLoader, value, null);
        RefreshJavaDisplay();
    }

    /// <summary>
    /// Exibe na UI qual runtime Java será usado para a versão de jogo selecionada.
    /// </summary>
    private void RefreshJavaDisplay()
    {
        try
        {
            var available = JavaDetector.FindAvailableJavaInstallations();
            var recommended = JavaDetector.GetRecommendedJava(SelectedGameVersion, available);
            if (recommended != null)
            {
                string arch = recommended.Is64Bit ? "64-bit" : "32-bit";
                SelectedJavaDisplay = $"Java {recommended.MajorVersion} • {recommended.Vendor} ({arch})";
            }
            else
            {
                SelectedJavaDisplay = "Nenhum Java compatível detectado — verifique JAVA_HOME ou PATH";
            }
        }
        catch
        {
            SelectedJavaDisplay = "Detecção de Java indisponível";
        }
    }

    private void UpdateLoaderVersionsListSync(string? preserveSelected = null)
    {
        var fallback = VersionCatalogService.GetVersionsForLoader(SelectedLoader);
        ApplyLoaderVersionsList(fallback, preserveSelected);
    }

    private async Task RefreshLoaderVersionsOnlineAsync(string loaderType, string gameVersion, string? preserveSelected)
    {
        _loaderFetchCts?.Cancel();
        _loaderFetchCts = new CancellationTokenSource();
        var token = _loaderFetchCts.Token;

        try
        {
            var versions = await VersionCatalogService.GetVersionsForLoaderAsync(loaderType, gameVersion, token);
            if (token.IsCancellationRequested) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (SelectedLoader == loaderType && SelectedGameVersion == gameVersion)
                {
                    ApplyLoaderVersionsList(versions, preserveSelected);
                }
            });
        }
        catch
        {
            // Ignora cancelamentos/falhas de rede e mantém fallback local
        }
    }

    private void ApplyLoaderVersionsList(System.Collections.Generic.IEnumerable<string> versions, string? preserveSelected)
    {
        AvailableLoaderVersions.Clear();
        foreach (var v in versions)
        {
            AvailableLoaderVersions.Add(v);
        }

        if (!string.IsNullOrWhiteSpace(preserveSelected) && SelectedLoader != "vanilla")
        {
            if (!AvailableLoaderVersions.Contains(preserveSelected))
                AvailableLoaderVersions.Insert(0, preserveSelected);

            SelectedLoaderVersion = preserveSelected;
        }
        else
        {
            SelectedLoaderVersion = AvailableLoaderVersions.FirstOrDefault() ?? "1.0";
        }
    }

    partial void OnSelectedJvmPresetChanged(string value)
    {
        UpdateJvmArgsFromPreset();
    }

    private void UpdateJvmArgsFromPreset()
    {
        if (JvmPresets.Presets.TryGetValue(SelectedJvmPreset, out var flags))
        {
            CustomJvmArgsText = string.Join(" ", flags);
        }
    }

    partial void OnSelectedAccountChanged(AccountProfile? value)
    {
        if (value != null && _accountManager.ActiveAccount?.Id != value.Id)
        {
            _accountManager.SelectActiveAccount(value.Id);
        }
    }
}
