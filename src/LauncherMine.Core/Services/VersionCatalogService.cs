using System.Net.Http.Json;
using System.Text.Json;

namespace LauncherMine.Core.Services;

/// <summary>
/// Catálogo completo de versões oficiais da Mojang (Vanilla), NeoForge, Fabric e Forge.
/// Realiza busca dinâmica online com fallback embutido offline.
/// </summary>
public static class VersionCatalogService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static readonly List<string> DefaultVanillaReleases = [
        "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.2", "1.20.1",
        "1.19.4", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.12.2", "1.8.9", "1.7.10"
    ];

    public static readonly List<string> DefaultNeoForgeVersions = [
        "21.1.88", "21.1.84", "21.1.65", "21.0.167", "20.6.119", "20.4.237"
    ];

    public static readonly List<string> DefaultFabricVersions = [
        "0.16.9", "0.16.5", "0.16.0", "0.15.11", "0.15.7", "0.14.25"
    ];

    public static readonly List<string> DefaultForgeVersions = [
        "51.0.33", "47.3.0", "43.3.0", "36.2.39", "31.2.57", "14.23.5.2860"
    ];

    /// <summary>
    /// Obtém todas as versões Vanilla lançadas pela Mojang.
    /// </summary>
    public static async Task<List<string>> GetVanillaVersionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await HttpClient.GetAsync(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", 
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), 
                    cancellationToken: cancellationToken);

                var versions = new List<string>();
                if (doc.RootElement.TryGetProperty("versions", out var array))
                {
                    foreach (var elem in array.EnumerateArray())
                    {
                        if (elem.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "release")
                        {
                            if (elem.TryGetProperty("id", out var idProp))
                            {
                                string? ver = idProp.GetString();
                                if (!string.IsNullOrEmpty(ver))
                                    versions.Add(ver);
                            }
                        }
                    }
                }

                if (versions.Count > 0)
                    return versions;
            }
        }
        catch
        {
            // Em caso de falha de rede, usa o catálogo padrão embutido
        }

        return DefaultVanillaReleases;
    }

    /// <summary>
    /// Obtém versões disponíveis para o loader escolhido.
    /// </summary>
    public static List<string> GetVersionsForLoader(string loaderType)
    {
        return loaderType.ToLowerInvariant() switch
        {
            "neoforge" => DefaultNeoForgeVersions,
            "fabric" => DefaultFabricVersions,
            "forge" => DefaultForgeVersions,
            _ => ["1.0"]
        };
    }
}
