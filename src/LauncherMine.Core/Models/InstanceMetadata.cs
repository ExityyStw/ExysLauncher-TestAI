using System.Text.Json.Serialization;

namespace LauncherMine.Core.Models;

public sealed class InstanceMetadata
{
    [JsonPropertyName("id")]
    public required string Id { get; init; } // Nome canônico isolado da pasta

    [JsonPropertyName("name")]
    public required string Name { get; set; } // Nome amigável de exibição

    [JsonPropertyName("gameVersion")]
    public required string GameVersion { get; set; } // Ex: "1.21.1"

    [JsonPropertyName("loaderType")]
    public string LoaderType { get; set; } = "neoforge"; // "vanilla", "neoforge", "fabric"

    [JsonPropertyName("loaderVersion")]
    public string? LoaderVersion { get; set; } // Ex: "21.1.84"

    [JsonPropertyName("customJavaPath")]
    public string? CustomJavaPath { get; set; } // Nulo = Detecção automática recomendada

    [JsonPropertyName("minMemoryMb")]
    public int MinMemoryMb { get; set; } = 2048;

    [JsonPropertyName("maxMemoryMb")]
    public int MaxMemoryMb { get; set; } = 6144; // 6 GB padrão para modpacks pesados

    [JsonPropertyName("extraJvmArgs")]
    public List<string> ExtraJvmArgs { get; set; } = [
        "-XX:+UseG1GC",
        "-XX:+ParallelRefProcEnabled",
        "-XX:MaxGCPauseMillis=200",
        "-XX:+UnlockExperimentalVMOptions",
        "-XX:+DisableExplicitGC",
        "-XX:+AlwaysPreTouch",
        "-XX:G1NewSizePercent=30",
        "-XX:G1MaxNewSizePercent=40",
        "-XX:G1ReservePercent=20",
        "-XX:G1HeapWastePercent=5",
        "-XX:G1MixedGCCountTarget=4",
        "-XX:InitiatingHeapOccupancyPercent=15",
        "-XX:G1MixedGCLiveThresholdPercent=90",
        "-XX:G1RSetUpdatingPauseTimePercent=5",
        "-XX:SurvivorRatio=32"
    ];

    [JsonPropertyName("closeLauncherOnLaunch")]
    public bool CloseLauncherOnLaunch { get; set; } = false; // Padrão desativado para diagnóstico

    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("lastPlayedDate")]
    public DateTime? LastPlayedDate { get; set; }
}
