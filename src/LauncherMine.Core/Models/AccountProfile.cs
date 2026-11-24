using System.Text.Json.Serialization;

namespace LauncherMine.Core.Models;

public enum AccountType
{
    Offline,
    Microsoft
}

public sealed class AccountProfile
{
    [JsonPropertyName("id")]
    public required string Id { get; init; } // UUID canônico

    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("type")]
    public required AccountType Type { get; init; }

    [JsonPropertyName("vaultKey")]
    public string? VaultKey { get; init; } // Chave de referência dentro do ISecureVault

    [JsonPropertyName("lastUsed")]
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;

    // Propriedade volátil mantida apenas em memória RAM durante a sessão
    [JsonIgnore]
    public string? CachedAccessToken { get; set; }

    [JsonIgnore]
    public DateTime? TokenExpiration { get; set; }

    public bool IsTokenValid => 
        !string.IsNullOrEmpty(CachedAccessToken) && 
        TokenExpiration.HasValue && 
        DateTime.UtcNow < TokenExpiration.Value;
}
