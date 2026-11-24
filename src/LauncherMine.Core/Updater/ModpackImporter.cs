using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LauncherMine.Core.Launch;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;

namespace LauncherMine.Core.Updater;

public sealed record CurseForgeManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("minecraft")] CurseForgeMinecraftInfo Minecraft,
    [property: JsonPropertyName("files")] List<CurseForgeFileEntry>? Files,
    [property: JsonPropertyName("overrides")] string OverridesFolder = "overrides"
);

public sealed record CurseForgeMinecraftInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("modLoaders")] List<CurseForgeModLoaderInfo> ModLoaders
);

public sealed record CurseForgeModLoaderInfo(
    [property: JsonPropertyName("id")] string Id, // Ex: "neoforge-21.1.84" ou "forge-47.2.0"
    [property: JsonPropertyName("primary")] bool Primary
);

public sealed record CurseForgeFileEntry(
    [property: JsonPropertyName("projectID")] int ProjectId,
    [property: JsonPropertyName("fileID")] int FileId,
    [property: JsonPropertyName("required")] bool Required = true
);

/// <summary>
/// Importador de modpacks no formato CurseForge / Modrinth / Overrides ZIP.
/// Protegido estritamente contra ataques de Zip Slip (CWE-29).
/// </summary>
public sealed class ModpackImporter
{
    private readonly InstanceManager _instanceManager;

    public ModpackImporter(InstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }

    /// <summary>
    /// Importa um arquivo .zip de modpack (CurseForge/Modrinth), descompacta os overrides de forma segura
    /// e cria uma instância isolada dedicada.
    /// </summary>
    public async Task<InstanceMetadata> ImportFromZipAsync(
        string zipFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("O arquivo de modpack .zip selecionado não foi encontrado.", zipFilePath);

        progress?.Report("Lendo arquivo ZIP do modpack...");

        using var zipArchive = ZipFile.OpenRead(zipFilePath);

        // 1. Tenta identificar se é um pacote CurseForge (manifest.json)
        var manifestEntry = zipArchive.GetEntry("manifest.json");
        string packName = Path.GetFileNameWithoutExtension(zipFilePath);
        string gameVersion = "1.21.1";
        string loaderType = "neoforge";
        string loaderVersion = "21.1.84";
        string overridesFolder = "overrides";

        if (manifestEntry != null)
        {
            progress?.Report("Identificado modpack formato CurseForge. Lendo manifesto...");
            await using var stream = manifestEntry.Open();
            var cfManifest = await JsonSerializer.DeserializeAsync<CurseForgeManifest>(stream, cancellationToken: cancellationToken);

            if (cfManifest != null)
            {
                packName = !string.IsNullOrWhiteSpace(cfManifest.Name) ? cfManifest.Name : packName;
                gameVersion = cfManifest.Minecraft.Version;
                overridesFolder = cfManifest.OverridesFolder ?? "overrides";

                var primaryLoader = cfManifest.Minecraft.ModLoaders.FirstOrDefault(l => l.Primary) 
                                    ?? cfManifest.Minecraft.ModLoaders.FirstOrDefault();

                if (primaryLoader != null)
                {
                    string loaderId = primaryLoader.Id.ToLowerInvariant();
                    if (loaderId.StartsWith("neoforge-"))
                    {
                        loaderType = "neoforge";
                        loaderVersion = loaderId["neoforge-".Length..];
                    }
                    else if (loaderId.StartsWith("fabric-"))
                    {
                        loaderType = "fabric";
                        loaderVersion = loaderId["fabric-".Length..];
                    }
                    else if (loaderId.StartsWith("forge-"))
                    {
                        loaderType = "forge";
                        loaderVersion = loaderId["forge-".Length..];
                    }
                }
            }
        }

        // 2. Cria a nova instância isolada
        progress?.Report($"Criando instância isolada '{packName}'...");
        var instance = await _instanceManager.CreateInstanceAsync(packName, gameVersion, loaderType, loaderVersion);
        string targetMinecraftDir = _instanceManager.GetInstanceMinecraftDirectory(instance.Id);

        // 3. Extração Segura dos Overrides com Proteção Anti-Zip Slip (CWE-29)
        progress?.Report("Extraindo mods, configs, scripts e recursos com validação de segurança...");
        string overridesPrefix = overridesFolder.TrimEnd('/') + "/";

        int totalEntries = zipArchive.Entries.Count;
        int current = 0;

        foreach (var entry in zipArchive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;

            // Extrai arquivos que estão dentro de overrides/ ou diretamente na raiz caso não use subpasta
            string relativeTarget;
            if (entry.FullName.StartsWith(overridesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                relativeTarget = entry.FullName[overridesPrefix.Length..];
            }
            else if (entry.FullName.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ||
                     entry.FullName.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ||
                     entry.FullName.StartsWith("kubejs/", StringComparison.OrdinalIgnoreCase) ||
                     entry.FullName.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) ||
                     entry.FullName.StartsWith("defaultconfigs/", StringComparison.OrdinalIgnoreCase))
            {
                relativeTarget = entry.FullName;
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(relativeTarget) || relativeTarget.EndsWith('/'))
                continue;

            // Validação estrita de fronteira contra Zip Slip (impede ../)
            string safeDestPath = PathSecurity.ResolveSecurePath(targetMinecraftDir, relativeTarget);

            string? dir = Path.GetDirectoryName(safeDestPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            entry.ExtractToFile(safeDestPath, overwrite: true);

            if (current % 15 == 0)
            {
                progress?.Report($"Extraindo: {Path.GetFileName(safeDestPath)} ({current}/{totalEntries})");
            }
        }

        progress?.Report("Modpack importado com sucesso!");
        return instance;
    }
}
