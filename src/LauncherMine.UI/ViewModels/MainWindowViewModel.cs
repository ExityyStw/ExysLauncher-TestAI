using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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

    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0 = Jogar, 1 = Instâncias, 2 = JVM & Otimizações, 3 = Importar Modpack

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
    private bool _closeLauncherOnLaunch = false;

    [ObservableProperty]
    private bool _openConsoleOnLaunch = false;

    [ObservableProperty]
    private string _customJvmArgsText = string.Empty;

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

    public ObservableCollection<AccountProfile> Accounts { get; } = [];

    [ObservableProperty]
    private AccountProfile? _selectedAccount;

    public ObservableCollection<InstanceMetadata> Instances { get; } = [];

    [ObservableProperty]
    private InstanceMetadata? _selectedInstance;

    [ObservableProperty]
    private string _importZipPath = string.Empty;

    public event Action<string>? LogReceived;
    public event Action? RequestOpenConsole;

    public MainWindowViewModel()
    {
        _secureVault = SecureVaultFactory.Create();
        _accountManager = new AccountManager(_secureVault);
        _instanceManager = new InstanceManager();
        _minecraftLauncher = new MinecraftLauncher(_instanceManager, _accountManager);
        _updater = new ModpackUpdater(maxConcurrentDownloads: 6);
        _importer = new ModpackImporter(_instanceManager);

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

        // 3. Preenche Versões
        foreach (var v in VersionCatalogService.DefaultVanillaReleases)
        {
            AvailableGameVersions.Add(v);
        }
        UpdateLoaderVersionsList();
    }

    private void DetectSystemRam()
    {
        TotalSystemRamMb = SystemInfoHelper.GetTotalPhysicalRamMb();
        MaxRamSliderMb = SystemInfoHelper.GetSafeMaxRamMb(TotalSystemRamMb);
        MinRamSliderMb = 1024;

        double totalGb = Math.Round(TotalSystemRamMb / 1024.0, 1);
        double maxGb = Math.Round(MaxRamSliderMb / 1024.0, 1);

        // Seleciona um valor padrão inteligente com base na RAM do usuário
        if (TotalSystemRamMb >= 16384)
            RamAllocationMb = 6144; // 6 GB padrão se tiver 16 GB+
        else if (TotalSystemRamMb >= 8192)
            RamAllocationMb = 4096; // 4 GB padrão se tiver 8 GB
        else
            RamAllocationMb = 2048; // 2 GB se tiver menos

        RamInfoText = $"Seu computador possui {totalGb} GB de RAM física detectada. Limite seguro: até {maxGb} GB.";
    }

    public async Task InitializeAsync()
    {
        await _accountManager.LoadAccountsAsync();
        SyncAccountsFromManager();

        if (Accounts.Count == 0)
        {
            var demoAccount = OfflineAuthService.CreateOfflineAccount("Steve");
            await _accountManager.AddOrUpdateAccountAsync(demoAccount);
        }

        await _instanceManager.LoadInstancesAsync();
        SyncInstancesFromManager();

        if (Instances.Count == 0)
        {
            var defaultInstance = await _instanceManager.CreateInstanceAsync(
                "NeoForge 1.21.1 Pack", 
                "1.21.1", 
                "neoforge", 
                "21.1.84");
            
            SyncInstancesFromManager();
            SelectedInstance = defaultInstance;
        }
        else
        {
            SelectedInstance = Instances[0];
        }

        if (SelectedInstance != null)
        {
            RamAllocationMb = Math.Clamp(SelectedInstance.MaxMemoryMb, MinRamSliderMb, MaxRamSliderMb);
            SelectedLoader = SelectedInstance.LoaderType;
            SelectedLoaderVersion = SelectedInstance.LoaderVersion ?? "21.1.84";
        }

        // Busca versões online em segundo plano sem travar a interface
        _ = Task.Run(async () =>
        {
            var onlineVersions = await VersionCatalogService.GetVanillaVersionsAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AvailableGameVersions.Clear();
                foreach (var v in onlineVersions)
                    AvailableGameVersions.Add(v);
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
    }

    public void SyncInstancesFromManager()
    {
        Instances.Clear();
        foreach (var inst in _instanceManager.Instances)
        {
            Instances.Add(inst);
        }
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
            SelectedInstance.LoaderType = SelectedLoader;
            SelectedInstance.LoaderVersion = SelectedLoaderVersion;

            if (!string.IsNullOrWhiteSpace(CustomJvmArgsText))
            {
                SelectedInstance.ExtraJvmArgs = [.. CustomJvmArgsText.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
            }

            await _instanceManager.SaveInstanceMetadataAsync(SelectedInstance);

            // 1. Sincronização Prévia do Modpack (se configurado)
            string mcDir = _instanceManager.GetInstanceMinecraftDirectory(SelectedInstance.Id);
            string localManifestPath = Path.Combine(Directory.GetCurrentDirectory(), "sample-manifest.json");

            if (File.Exists(localManifestPath))
            {
                try
                {
                    string manifestJson = await File.ReadAllTextAsync(localManifestPath);
                    var manifest = JsonSerializer.Deserialize<ModpackManifest>(manifestJson);

                    if (manifest != null && manifest.Files != null && manifest.Files.Count > 0)
                    {
                        StatusMessage = "Sincronizando modpack (Validação SHA-256)...";
                        var syncProgress = new Progress<UpdateProgress>(report =>
                        {
                            ProgressValue = report.ProgressPercentage;
                            StatusMessage = $"[Validação] {report.ProcessedFiles}/{report.TotalFiles} ({report.CurrentFileName})";
                        });

                        await _updater.SynchronizeAsync(mcDir, manifest, syncProgress);
                    }
                }
                catch (Exception syncEx)
                {
                    LogReceived?.Invoke($"[AVISO] Falha ao verificar manifesto: {syncEx.Message}");
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
        var account = OfflineAuthService.CreateOfflineAccount(username);
        await _accountManager.AddOrUpdateAccountAsync(account);
    }

    [RelayCommand]
    public async Task RemoveAccountAsync()
    {
        if (SelectedAccount != null)
        {
            await _accountManager.RemoveAccountAsync(SelectedAccount.Id);
        }
    }

    partial void OnSelectedLoaderChanged(string value)
    {
        UpdateLoaderVersionsList();
    }

    private void UpdateLoaderVersionsList()
    {
        AvailableLoaderVersions.Clear();
        foreach (var v in VersionCatalogService.GetVersionsForLoader(SelectedLoader))
        {
            AvailableLoaderVersions.Add(v);
        }
        SelectedLoaderVersion = AvailableLoaderVersions.FirstOrDefault() ?? "1.0";
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
