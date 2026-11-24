using System.Text.Json.Serialization;

namespace LauncherMine.Core.Models;

public sealed record ModpackManifest
{
    [JsonPropertyName("modpackName")]
    public required string ModpackName { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("gameVersion")]
    public required string GameVersion { get; init; }

    [JsonPropertyName("loader")]
    public required ModLoaderInfo Loader { get; init; }

    [JsonPropertyName("files")]
    public required List<ManifestFileEntry> Files { get; init; }
}

public sealed record ModLoaderInfo
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // "neoforge", "fabric", "forge"

    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

public sealed record ManifestFileEntry
{
    [JsonPropertyName("path")]
    public required string RelativePath { get; init; } // Ex: "mods/sodium-neoforge-0.6.jar"

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("size")]
    public required long Size { get; init; }

    [JsonPropertyName("url")]
    public required string DownloadUrl { get; init; }

    [JsonPropertyName("optional")]
    public bool IsOptional { get; init; } = false;
}
