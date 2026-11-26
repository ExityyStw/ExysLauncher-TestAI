using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using LauncherMine.Core.Auth;
using LauncherMine.Core.Models;

namespace LauncherMine.Core.Launch;

public sealed record LaunchProgress(string StepDescription, int Percentage);

/// <summary>
/// Orquestrador de execução de alto desempenho e segurança contra Command Injection.
/// Suporta Vanilla, NeoForge, Fabric e Forge com instalação automática pré-execução.
/// </summary>
public sealed partial class MinecraftLauncher
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly InstanceManager _instanceManager;
    private readonly AccountManager _accountManager;

    [GeneratedRegex(@"^[a-zA-Z0-9._+-]+$")]
    private static partial Regex SafeVersionRegex();

    public event Action<string>? GameLogReceived;
    public event Action<int>? GameExited;

    public MinecraftLauncher(InstanceManager instanceManager, AccountManager accountManager)
    {
        _instanceManager = instanceManager;
        _accountManager = accountManager;
    }

    /// <summary>
    /// Prepara o mod loader (se aplicável), baixa assets/bibliotecas e inicia o processo Java.
    /// </summary>
    public async Task<Process> LaunchAsync(
        InstanceMetadata instance,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var account = _accountManager.ActiveAccount
            ?? throw new InvalidOperationException("Nenhuma conta selecionada para inicializar o jogo.");

        progress?.Report(new LaunchProgress("Autenticando sessão...", 5));
        string accessToken = account.Type == AccountType.Offline
            ? "OFFLINE"
            : await _accountManager.GetValidAccessTokenAsync(account, cancellationToken);

        // 1. Resolução do Java Seguro
        progress?.Report(new LaunchProgress("Detectando runtime do Java...", 12));
        string javaBinary = ResolveJavaBinary(instance);

        // 2. Isolamento de Diretórios
        string mcDir = _instanceManager.GetInstanceMinecraftDirectory(instance.Id);
        var minecraftPath = new MinecraftPath(mcDir);
        var launcher = new CmlLib.Core.MinecraftLauncher(minecraftPath);

        // 3. Resolução de Sessão (contas offline usam sessão offline nativa do CmlLib)
        var session = account.Type == AccountType.Offline
            ? MSession.CreateOfflineSession(account.Username)
            : new MSession(account.Username, accessToken, account.Id);

        // 4. Montagem das Opções de Inicialização
        var launchOptions = new MLaunchOption
        {
            Path = minecraftPath,
            JavaPath = javaBinary,
            Session = session,
            MinimumRamMb = instance.MinMemoryMb,
            MaximumRamMb = instance.MaxMemoryMb,
            VersionType = $"ExysLauncher-{instance.LoaderType}",
            ExtraJvmArguments = instance.ExtraJvmArgs.Select(arg => new MArgument(arg)).ToArray()
        };

        // Conecta os eventos de progresso dos downloads do Minecraft (Assets, Bibliotecas, Nativos)
        launcher.FileProgressChanged += (_, args) =>
        {
            int total = Math.Max(args.TotalTasks, 1);
            int current = args.ProgressedTasks;
            int percentage = (int)((double)current / total * 100);
            progress?.Report(new LaunchProgress($"Baixando arquivos: {args.Name} ({current}/{total})", percentage));
        };

        launcher.ByteProgressChanged += (_, args) =>
        {
            if (args.TotalBytes > 0)
            {
                int percentage = (int)((double)args.ProgressedBytes / args.TotalBytes * 100);
                progress?.Report(new LaunchProgress("Baixando assets e bibliotecas...", percentage));
            }
        };

        // 5. Garante instalação automática do Mod Loader (NeoForge / Fabric / Forge)
        string targetVersion = await EnsureLoaderInstalledAsync(
            launcher, mcDir, instance, javaBinary, launchOptions, progress, cancellationToken);

        progress?.Report(new LaunchProgress($"Verificando arquivos de '{targetVersion}'...", 70));

        // Constrói o processo através de InstallAndBuildProcessAsync paramétrico (Zero Shell Injection)
        var process = await launcher.InstallAndBuildProcessAsync(targetVersion, launchOptions);

        // 6. Configurações de Monitoramento de Processo
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) GameLogReceived?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) GameLogReceived?.Invoke($"[STDERR] {e.Data}");
        };

        progress?.Report(new LaunchProgress("Iniciando processo do jogo...", 95));

        bool started = process.Start();
        if (!started)
        {
            throw new InvalidOperationException("Falha ao iniciar o processo Java do Minecraft.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        instance.LastPlayedDate = DateTime.UtcNow;

        // Eficiência Extrema: Fecha o launcher se configurado
        if (instance.CloseLauncherOnLaunch)
        {
            progress?.Report(new LaunchProgress("Jogo iniciado! Liberando recursos de memória...", 100));
            await Task.Delay(1500, cancellationToken);
            Environment.Exit(0);
        }

        process.Exited += (_, _) =>
        {
            GameExited?.Invoke(process.ExitCode);
        };

        progress?.Report(new LaunchProgress("Jogo em execução!", 100));
        return process;
    }

    private async Task<string> EnsureLoaderInstalledAsync(
        CmlLib.Core.MinecraftLauncher launcher,
        string mcDir,
        InstanceMetadata instance,
        string javaBinary,
        MLaunchOption launchOptions,
        IProgress<LaunchProgress>? progress,
        CancellationToken ct)
    {
        string loaderType = (instance.LoaderType ?? "vanilla").Trim().ToLowerInvariant();
        string gameVersion = instance.GameVersion;
        string? loaderVersion = instance.LoaderVersion;

        if (loaderType == "vanilla" || string.IsNullOrWhiteSpace(loaderVersion))
            return gameVersion;

        if (!SafeVersionRegex().IsMatch(gameVersion) || !SafeVersionRegex().IsMatch(loaderVersion))
            throw new InvalidOperationException("Versão do jogo ou do loader contém caracteres inválidos.");

        // Verifica se já existe perfil instalado na pasta versions/
        string? existingId = FindInstalledLoaderVersionId(mcDir, gameVersion, loaderType, loaderVersion);
        if (existingId != null)
            return existingId;

        if (loaderType == "fabric")
        {
            return await InstallFabricProfileAsync(mcDir, gameVersion, loaderVersion, progress, ct);
        }

        if (loaderType is "neoforge" or "forge")
        {
            return await InstallForgeStyleLoaderAsync(
                launcher, mcDir, gameVersion, loaderType, loaderVersion, javaBinary, launchOptions, progress, ct);
        }

        return gameVersion;
    }

    private static string? FindInstalledLoaderVersionId(
        string mcDir,
        string gameVersion,
        string loaderType,
        string loaderVersion)
    {
        string versionsDir = Path.Combine(mcDir, "versions");
        if (!Directory.Exists(versionsDir))
            return null;

        string[] candidates = loaderType switch
        {
            "fabric" => [$"fabric-loader-{loaderVersion}-{gameVersion}"],
            "neoforge" => [$"neoforge-{loaderVersion}", $"{gameVersion}-neoforge-{loaderVersion}"],
            "forge" => [$"{gameVersion}-forge-{loaderVersion}"],
            _ => []
        };

        foreach (var id in candidates)
        {
            if (File.Exists(Path.Combine(versionsDir, id, $"{id}.json")))
                return id;
        }

        return null;
    }

    private static async Task<string> InstallFabricProfileAsync(
        string mcDir,
        string gameVersion,
        string loaderVersion,
        IProgress<LaunchProgress>? progress,
        CancellationToken ct)
    {
        progress?.Report(new LaunchProgress($"Baixando perfil Fabric {loaderVersion} ({gameVersion})...", 30));

        string url = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";
        using var response = await HttpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Não foi possível baixar o perfil do Fabric {loaderVersion} para Minecraft {gameVersion} (HTTP {(int)response.StatusCode}).");
        }

        string profileJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(profileJson);
        string profileId = doc.RootElement.TryGetProperty("id", out var idProp) && idProp.GetString() is string parsed
            ? parsed
            : $"fabric-loader-{loaderVersion}-{gameVersion}";

        string versionDir = Path.Combine(mcDir, "versions", profileId);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, $"{profileId}.json"), profileJson, ct);

        return profileId;
    }

    private async Task<string> InstallForgeStyleLoaderAsync(
        CmlLib.Core.MinecraftLauncher launcher,
        string mcDir,
        string gameVersion,
        string loaderType,
        string loaderVersion,
        string javaBinary,
        MLaunchOption launchOptions,
        IProgress<LaunchProgress>? progress,
        CancellationToken ct)
    {
        // Forge/NeoForge installers exigem launcher_profiles.json e o Vanilla instalado
        string profilesFile = Path.Combine(mcDir, "launcher_profiles.json");
        if (!File.Exists(profilesFile))
        {
            await File.WriteAllTextAsync(profilesFile, "{\"profiles\":{}}", ct);
        }

        progress?.Report(new LaunchProgress($"Preparando Minecraft {gameVersion} base...", 25));
        await launcher.InstallAndBuildProcessAsync(gameVersion, launchOptions);

        string installerUrl = loaderType == "neoforge"
            ? $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{Uri.EscapeDataString(loaderVersion)}/neoforge-{Uri.EscapeDataString(loaderVersion)}-installer.jar"
            : $"https://maven.minecraftforge.net/net/minecraftforge/forge/{Uri.EscapeDataString(gameVersion)}-{Uri.EscapeDataString(loaderVersion)}/forge-{Uri.EscapeDataString(gameVersion)}-{Uri.EscapeDataString(loaderVersion)}-installer.jar";

        string tempInstaller = Path.Combine(Path.GetTempPath(), $"exys_{loaderType}_{Guid.NewGuid():N}.jar");

        try
        {
            progress?.Report(new LaunchProgress($"Baixando instalador {loaderType} {loaderVersion}...", 40));
            using var response = await HttpClient.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Instalador {loaderType} {loaderVersion} para {gameVersion} não encontrado (HTTP {(int)response.StatusCode}).");
            }

            await using (var fs = File.Create(tempInstaller))
            {
                await response.Content.CopyToAsync(fs, ct);
            }

            progress?.Report(new LaunchProgress($"Instalando {loaderType} {loaderVersion} (aguarde)...", 55));

            var psi = new ProcessStartInfo
            {
                FileName = javaBinary,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = mcDir
            };
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(tempInstaller);
            psi.ArgumentList.Add("--installClient");
            psi.ArgumentList.Add(mcDir);

            using var installProc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Falha ao executar o instalador do {loaderType}.");

            installProc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) GameLogReceived?.Invoke($"[INSTALL] {e.Data}");
            };
            installProc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) GameLogReceived?.Invoke($"[INSTALL-ERR] {e.Data}");
            };
            installProc.BeginOutputReadLine();
            installProc.BeginErrorReadLine();

            await installProc.WaitForExitAsync(ct);

            if (installProc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"O instalador do {loaderType} encerrou com código {installProc.ExitCode}.");
            }

            string? installedId = FindInstalledLoaderVersionId(mcDir, gameVersion, loaderType, loaderVersion);
            if (installedId != null)
                return installedId;

            // Fallback: procura qualquer diretório de versão recém-criado que contenha o nome do loader
            string versionsDir = Path.Combine(mcDir, "versions");
            if (Directory.Exists(versionsDir))
            {
                var match = Directory.GetDirectories(versionsDir)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(name =>
                        name != null &&
                        name.Contains(loaderType, StringComparison.OrdinalIgnoreCase) &&
                        name.Contains(loaderVersion, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(Path.Combine(versionsDir, name, $"{name}.json")));

                if (match != null)
                    return match;
            }

            throw new InvalidOperationException(
                $"O {loaderType} {loaderVersion} foi instalado, mas o perfil JSON não foi encontrado em versions/.");
        }
        finally
        {
            try
            {
                if (File.Exists(tempInstaller))
                    File.Delete(tempInstaller);
            }
            catch
            {
                // Ignora erro ao limpar arquivo temporário
            }
        }
    }

    private static string ResolveJavaBinary(InstanceMetadata instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.CustomJavaPath) && File.Exists(instance.CustomJavaPath))
        {
            return Path.GetFullPath(instance.CustomJavaPath);
        }

        var available = JavaDetector.FindAvailableJavaInstallations();
        var recommended = JavaDetector.GetRecommendedJava(instance.GameVersion, available);

        if (recommended != null)
        {
            return recommended.Path;
        }

        return OperatingSystem.IsWindows() ? "java.exe" : "java";
    }
}
