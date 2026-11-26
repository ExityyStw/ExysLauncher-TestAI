using System.Text.Json;
using System.Xml.Linq;

namespace LauncherMine.Core.Services;

/// <summary>
/// Catálogo completo de versões oficiais da Mojang (Vanilla), NeoForge, Fabric e Forge.
/// Realiza busca dinâmica online com fallback embutido offline e cache em memória.
/// </summary>
public static class VersionCatalogService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static readonly List<string> DefaultVanillaReleases = [
        "1.21.4", "1.21.3", "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.2", "1.20.1",
        "1.19.4", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.12.2", "1.8.9", "1.7.10"
    ];

    public static readonly List<string> DefaultNeoForgeVersions = [
        "21.4.84-beta", "21.1.92", "21.1.88", "21.1.84", "21.1.65", "21.0.167", "20.6.119", "20.4.237"
    ];

    public static readonly List<string> DefaultFabricVersions = [
        "0.16.10", "0.16.9", "0.16.5", "0.16.0", "0.15.11", "0.15.7", "0.14.25"
    ];

    public static readonly List<string> DefaultForgeVersions = [
        "54.0.26", "51.0.33", "47.3.12", "47.3.0", "43.4.0", "40.2.21", "36.2.42", "31.2.57", "14.23.5.2860"
    ];

    private static List<string>? _cachedVanillaReleases;
    private static List<string>? _cachedAllNeoForge;
    private static List<string>? _cachedAllForge;
    private static List<string>? _cachedAllFabric;

    public static IReadOnlyList<string> CachedVanillaReleases => _cachedVanillaReleases ?? DefaultVanillaReleases;

    /// <summary>
    /// Obtém todas as versões Vanilla (releases) lançadas pela Mojang.
    /// </summary>
    public static async Task<List<string>> GetVanillaVersionsAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedVanillaReleases is { Count: > 0 })
            return _cachedVanillaReleases;

        try
        {
            using var response = await HttpClient.GetAsync(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                var versions = new List<string>();
                if (doc.RootElement.TryGetProperty("versions", out var array))
                {
                    foreach (var elem in array.EnumerateArray())
                    {
                        if (elem.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "release" &&
                            elem.TryGetProperty("id", out var idProp))
                        {
                            string? ver = idProp.GetString();
                            if (!string.IsNullOrEmpty(ver))
                                versions.Add(ver);
                        }
                    }
                }

                if (versions.Count > 0)
                {
                    _cachedVanillaReleases = versions;
                    return versions;
                }
            }
        }
        catch
        {
            // Fallback offline abaixo
        }

        return DefaultVanillaReleases;
    }

    /// <summary>
    /// Busca todas as versões disponíveis online para o Mod Loader e versão do Minecraft escolhidos.
    /// </summary>
    public static async Task<List<string>> GetVersionsForLoaderAsync(
        string loaderType,
        string gameVersion,
        CancellationToken cancellationToken = default)
    {
        string normalizedLoader = (loaderType ?? "vanilla").Trim().ToLowerInvariant();
        if (normalizedLoader == "vanilla")
            return ["Vanilla (Sem Mod Loader)"];

        try
        {
            return normalizedLoader switch
            {
                "fabric" => await GetFabricVersionsAsync(gameVersion, cancellationToken),
                "neoforge" => await GetNeoForgeVersionsAsync(gameVersion, cancellationToken),
                "forge" => await GetForgeVersionsAsync(gameVersion, cancellationToken),
                _ => ["1.0"]
            };
        }
        catch
        {
            return GetVersionsForLoader(normalizedLoader);
        }
    }

    /// <summary>
    /// Versões síncronas imediatas (cache + fallback embutido).
    /// </summary>
    public static List<string> GetVersionsForLoader(string loaderType)
    {
        return (loaderType ?? "vanilla").ToLowerInvariant() switch
        {
            "neoforge" => _cachedAllNeoForge is { Count: > 0 } ? _cachedAllNeoForge : DefaultNeoForgeVersions,
            "fabric" => _cachedAllFabric is { Count: > 0 } ? _cachedAllFabric : DefaultFabricVersions,
            "forge" => DefaultForgeVersions,
            "vanilla" => ["Vanilla (Sem Mod Loader)"],
            _ => ["1.0"]
        };
    }

    private static async Task<List<string>> GetFabricVersionsAsync(string gameVersion, CancellationToken ct)
    {
        string url = string.IsNullOrWhiteSpace(gameVersion)
            ? "https://meta.fabricmc.net/v2/versions/loader"
            : $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}";

        using var response = await HttpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(gameVersion))
        {
            // Fallback para lista global de loaders do Fabric
            using var fallbackResp = await HttpClient.GetAsync("https://meta.fabricmc.net/v2/versions/loader", ct);
            if (!fallbackResp.IsSuccessStatusCode)
                return DefaultFabricVersions;

            await using var fbStream = await fallbackResp.Content.ReadAsStreamAsync(ct);
            using var fbDoc = await JsonDocument.ParseAsync(fbStream, cancellationToken: ct);
            var globalList = new List<string>();
            foreach (var item in fbDoc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("version", out var vProp) && vProp.GetString() is string v)
                    globalList.Add(v);
            }

            if (globalList.Count > 0)
            {
                _cachedAllFabric = globalList;
                return globalList;
            }
            return DefaultFabricVersions;
        }

        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var list = new List<string>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("loader", out var loaderObj) &&
                    loaderObj.TryGetProperty("version", out var vProp) &&
                    vProp.GetString() is string ver)
                {
                    list.Add(ver);
                }
                else if (item.TryGetProperty("version", out var directVer) &&
                         directVer.GetString() is string dVer)
                {
                    list.Add(dVer);
                }
            }

            if (list.Count > 0)
                return list;
        }

        return DefaultFabricVersions;
    }

    private static async Task<List<string>> GetNeoForgeVersionsAsync(string gameVersion, CancellationToken ct)
    {
        if (_cachedAllNeoForge == null || _cachedAllNeoForge.Count == 0)
        {
            using var response = await HttpClient.GetAsync(
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
                ct);

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var xdoc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
                var all = xdoc.Descendants("version")
                    .Select(x => x.Value.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Reverse()
                    .ToList();

                if (all.Count > 0)
                    _cachedAllNeoForge = all;
            }
        }

        var source = _cachedAllNeoForge ?? DefaultNeoForgeVersions;
        string? prefix = GetNeoForgePrefixForGameVersion(gameVersion);
        if (!string.IsNullOrEmpty(prefix))
        {
            var filtered = source.Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count > 0)
                return filtered;
        }

        return source;
    }

    private static async Task<List<string>> GetForgeVersionsAsync(string gameVersion, CancellationToken ct)
    {
        if (_cachedAllForge == null || _cachedAllForge.Count == 0)
        {
            using var response = await HttpClient.GetAsync(
                "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
                ct);

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var xdoc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
                var all = xdoc.Descendants("version")
                    .Select(x => x.Value.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Reverse()
                    .ToList();

                if (all.Count > 0)
                    _cachedAllForge = all;
            }
        }

        if (_cachedAllForge is { Count: > 0 })
        {
            string mcPrefix = gameVersion + "-";
            var filtered = _cachedAllForge
                .Where(v => v.StartsWith(mcPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(v => v[mcPrefix.Length..])
                .ToList();

            if (filtered.Count > 0)
                return filtered;
        }

        return DefaultForgeVersions;
    }

    private static string? GetNeoForgePrefixForGameVersion(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
            return null;

        if (gameVersion == "1.20.1")
            return "47.1.";

        var parts = gameVersion.Split('.');
        if (parts.Length >= 2 && parts[0] == "1")
        {
            string minor = parts[1];
            string patch = parts.Length >= 3 ? parts[2] : "0";
            return $"{minor}.{patch}.";
        }

        return null;
    }
}
