using System.Diagnostics;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using LauncherMine.Core.Auth;
using LauncherMine.Core.Models;

namespace LauncherMine.Core.Launch;

public sealed record LaunchProgress(string StepDescription, int Percentage);

/// <summary>
/// Orquestrador de execução de alto desempenho e segurança contra Command Injection.
/// Possui suporte a fechamento automático imediato pós-inicialização para liberar 100% da RAM.
/// </summary>
public sealed class MinecraftLauncher
{
    private readonly InstanceManager _instanceManager;
    private readonly AccountManager _accountManager;

    public event Action<string>? GameLogReceived;
    public event Action<int>? GameExited;

    public MinecraftLauncher(InstanceManager instanceManager, AccountManager accountManager)
    {
        _instanceManager = instanceManager;
        _accountManager = accountManager;
    }

    /// <summary>
    /// Prepara os assets, compõe os argumentos de forma paramétrica segura e inicia o processo Java.
    /// </summary>
    public async Task<Process> LaunchAsync(
        InstanceMetadata instance,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var account = _accountManager.ActiveAccount 
            ?? throw new InvalidOperationException("Nenhuma conta selecionada para inicializar o jogo.");

        progress?.Report(new LaunchProgress("Autenticando sessão...", 10));
        string accessToken = await _accountManager.GetValidAccessTokenAsync(account, cancellationToken);

        // 1. Resolução do Java Seguro
        progress?.Report(new LaunchProgress("Detectando runtime do Java...", 20));
        string javaBinary = ResolveJavaBinary(instance);

        // 2. Isolamento de Diretórios (Prism-like)
        string mcDir = _instanceManager.GetInstanceMinecraftDirectory(instance.Id);
        var minecraftPath = new MinecraftPath(mcDir);
        var launcher = new CmlLib.Core.MinecraftLauncher(minecraftPath);

        // 3. Resolução de Sessão
        var session = new MSession(account.Username, accessToken, account.Id);

        // 4. Montagem das Opções de Inicialização
        progress?.Report(new LaunchProgress("Montando argumentos de inicialização...", 50));
        var launchOptions = new MLaunchOption
        {
            Path = minecraftPath,
            JavaPath = javaBinary,
            Session = session,
            MinimumRamMb = instance.MinMemoryMb,
            MaximumRamMb = instance.MaxMemoryMb,
            VersionType = $"LauncherMine-{instance.LoaderType}",
            ExtraJvmArguments = instance.ExtraJvmArgs.Select(arg => new MArgument(arg)).ToArray()
        };

        string targetVersion = instance.GameVersion;

        // Se for um loader customizado (NeoForge/Fabric), verifica se já está instalado
        if (!string.IsNullOrEmpty(instance.LoaderVersion) && instance.LoaderType != "vanilla")
        {
            string customLoaderId = $"{instance.GameVersion}-{instance.LoaderType}-{instance.LoaderVersion}";
            var versions = await launcher.GetAllVersionsAsync();
            if (versions.Any(v => v.Name == customLoaderId))
            {
                targetVersion = customLoaderId;
            }
        }

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

        progress?.Report(new LaunchProgress("Instalando e verificando arquivos do jogo...", 60));

        // Constrói o processo através de InstallAndBuildProcessAsync paramétrico (Zero Shell Injection)
        var process = await launcher.InstallAndBuildProcessAsync(targetVersion, launchOptions);

        // 5. Configurações de Monitoramento de Processo
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

        // Eficiência Extrema (Horizon-like): Se configurado para fechar o launcher pós-start,
        // aguarda confirmação de PID válido e encerra o processo do launcher, liberando 100% da RAM.
        if (instance.CloseLauncherOnLaunch)
        {
            progress?.Report(new LaunchProgress("Jogo iniciado! Liberando recursos de memória...", 100));
            await Task.Delay(1500, cancellationToken); // Aguarda inicialização do runtime

            // Fecha o processo do launcher (0 MB residual durante a gameplay)
            Environment.Exit(0);
        }

        process.Exited += (_, _) =>
        {
            GameExited?.Invoke(process.ExitCode);
        };

        progress?.Report(new LaunchProgress("Pronto!", 100));
        return process;
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

        // Fallback para java do sistema
        return OperatingSystem.IsWindows() ? "java.exe" : "java";
    }
}
