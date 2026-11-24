using System.Text.Json;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;

namespace LauncherMine.Core.Launch;

/// <summary>
/// Gerenciador de instâncias estritamente isoladas inspirado na arquitetura do Prism Launcher.
/// Cada instância reside em seu próprio diretório autônomo, isolando mods, saves, configs e assets.
/// </summary>
public sealed class InstanceManager
{
    private readonly string _instancesRoot;
    private readonly List<InstanceMetadata> _instances = [];

    public IReadOnlyList<InstanceMetadata> Instances => _instances.AsReadOnly();
    public string InstancesRootDirectory => _instancesRoot;

    public InstanceManager(string? customRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(customRoot))
        {
            _instancesRoot = customRoot;
        }
        else
        {
            string baseData = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

            _instancesRoot = Path.Combine(baseData, "LauncherMine", "instances");
        }

        Directory.CreateDirectory(_instancesRoot);
    }

    public async Task LoadInstancesAsync()
    {
        _instances.Clear();

        if (!Directory.Exists(_instancesRoot)) return;

        foreach (var dir in Directory.GetDirectories(_instancesRoot))
        {
            string metadataFile = Path.Combine(dir, "instance.json");
            if (File.Exists(metadataFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(metadataFile);
                    var metadata = JsonSerializer.Deserialize<InstanceMetadata>(json);
                    if (metadata != null)
                    {
                        _instances.Add(metadata);
                    }
                }
                catch
                {
                    // Ignora instâncias corrompidas
                }
            }
        }
    }

    /// <summary>
    /// Cria uma nova instância completamente isolada.
    /// </summary>
    public async Task<InstanceMetadata> CreateInstanceAsync(
        string name, 
        string gameVersion, 
        string loaderType = "neoforge", 
        string? loaderVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Gera um ID canônico seguro para o diretório
        string instanceId = SanitizeInstanceFolderName(name);
        string instanceDir = PathSecurity.ResolveSecurePath(_instancesRoot, instanceId);

        if (Directory.Exists(instanceDir))
        {
            instanceId = $"{instanceId}_{DateTime.UtcNow.Ticks}";
            instanceDir = PathSecurity.ResolveSecurePath(_instancesRoot, instanceId);
        }

        Directory.CreateDirectory(instanceDir);

        // Cria a subestrutura de isolamento Prism-like
        Directory.CreateDirectory(Path.Combine(instanceDir, "minecraft"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "minecraft", "mods"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "minecraft", "config"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "minecraft", "saves"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "minecraft", "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(instanceDir, "minecraft", "shaderpacks"));

        var metadata = new InstanceMetadata
        {
            Id = instanceId,
            Name = name,
            GameVersion = gameVersion,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion
        };

        string metadataPath = Path.Combine(instanceDir, "instance.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

        _instances.Add(metadata);
        return metadata;
    }

    public async Task DeleteInstanceAsync(string instanceId)
    {
        string dir = GetInstanceDirectory(instanceId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        _instances.RemoveAll(i => i.Id == instanceId);
        await Task.CompletedTask;
    }

    public void OpenInstanceFolder(string instanceId)
    {
        string dir = GetInstanceMinecraftDirectory(instanceId);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{dir}\"",
                UseShellExecute = true
            });
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{dir}\"",
                UseShellExecute = true
            });
        }
    }

    public async Task SaveInstanceMetadataAsync(InstanceMetadata metadata)
    {
        string instanceDir = GetInstanceDirectory(metadata.Id);
        string metadataPath = Path.Combine(instanceDir, "instance.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string GetInstanceDirectory(string instanceId)
    {
        return PathSecurity.ResolveSecurePath(_instancesRoot, instanceId);
    }

    public string GetInstanceMinecraftDirectory(string instanceId)
    {
        string instanceDir = GetInstanceDirectory(instanceId);
        return Path.Combine(instanceDir, "minecraft");
    }

    private static string SanitizeInstanceFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new(name.Where(c => !invalid.Contains(c) && !char.IsWhiteSpace(c)).ToArray());
        return string.IsNullOrEmpty(clean) ? "instance" : clean.ToLowerInvariant();
    }
}
