using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;

namespace LauncherMine.Core.Auth;

public sealed record DeviceCodeResponse(
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("verification_uri_complete")] string? VerificationUriComplete,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval
);

/// <summary>
/// Fluxo de autenticação oficial OAuth2 Microsoft / Xbox Live / Minecraft Services.
/// Os refresh tokens são isolados no ISecureVault criptografado do sistema operacional.
/// </summary>
public sealed class MicrosoftAuthService : IDisposable
{
    // Client ID público registrado para fluxos Xbox/Minecraft (mesmo usado por launchers OSS como Prism)
    private const string ClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";
    private const string Scope = "XboxLive.SignIn offline_access";

    private readonly HttpClient _httpClient;
    private readonly ISecureVault _secureVault;

    public MicrosoftAuthService(ISecureVault secureVault)
    {
        _secureVault = secureVault;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<DeviceCodeResponse> InitiateDeviceCodeLoginAsync(CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope
        };

        using var response = await _httpClient.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
            new FormUrlEncodedContent(parameters),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("Falha ao obter código de dispositivo da Microsoft.");
    }

    public async Task<AccountProfile> PollDeviceCodeResultAsync(
        DeviceCodeResponse deviceCode, 
        CancellationToken cancellationToken = default)
    {
        int pollInterval = Math.Max(deviceCode.Interval, 3);
        DateTime expireTime = DateTime.UtcNow.AddSeconds(deviceCode.ExpiresIn);

        while (DateTime.UtcNow < expireTime)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(pollInterval), cancellationToken);

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode.DeviceCode
            };

            using var response = await _httpClient.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                new FormUrlEncodedContent(parameters),
                cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorProp))
            {
                string error = errorProp.GetString() ?? "";
                if (error == "authorization_pending")
                    continue;
                if (error == "slow_down")
                {
                    pollInterval += 5;
                    continue;
                }
                throw new InvalidOperationException($"Erro no login Microsoft: {error}");
            }

            string msAccessToken = root.GetProperty("access_token").GetString()!;
            if (!root.TryGetProperty("refresh_token", out var rtProp) || rtProp.GetString() is not { Length: > 0 } msRefreshToken)
            {
                throw new InvalidOperationException("A Microsoft não retornou um refresh token. Verifique se o escopo 'offline_access' está habilitado no aplicativo.");
            }

            return await CompleteMinecraftLoginAsync(msAccessToken, msRefreshToken, cancellationToken);
        }

        throw new TimeoutException("O tempo de autorização do código expirou.");
    }

    public async Task<string> RefreshAccessTokenAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        if (profile.Type != AccountType.Microsoft || string.IsNullOrEmpty(profile.VaultKey))
            throw new InvalidOperationException("Esta conta não é do tipo Microsoft ou não possui chave de cofre.");

        string? refreshToken = await _secureVault.RetrieveSecretAsync(profile.VaultKey);
        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException("Refresh token expirado ou removido do cofre seguro. Faça login novamente.");

        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope
        };

        // Renovação no mesmo endpoint v2.0 do device flow (login.live.com é legado e incompatível)
        using var response = await _httpClient.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
            new FormUrlEncodedContent(parameters),
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorProp))
        {
            string errorCode = errorProp.GetString() ?? "unknown";
            string errorDesc = root.TryGetProperty("error_description", out var descProp)
                ? descProp.GetString() ?? string.Empty
                : string.Empty;
            throw new InvalidOperationException($"Falha ao renovar token Microsoft: {errorCode} — {errorDesc}");
        }

        response.EnsureSuccessStatusCode();

        string newAccessToken = root.GetProperty("access_token").GetString()!;
        // O Microsoft pode ou não rotacionar o refresh token; preserva o antigo se ausente
        string newRefreshToken = root.TryGetProperty("refresh_token", out var rtProp) && rtProp.GetString() is { Length: > 0 } rotated
            ? rotated
            : refreshToken;

        // Atualiza o refresh token criptografado
        await _secureVault.StoreSecretAsync(profile.VaultKey, newRefreshToken);

        var refreshedAccount = await CompleteMinecraftLoginAsync(newAccessToken, newRefreshToken, cancellationToken);
        profile.CachedAccessToken = refreshedAccount.CachedAccessToken;
        profile.TokenExpiration = refreshedAccount.TokenExpiration;

        return profile.CachedAccessToken!;
    }

    private async Task<AccountProfile> CompleteMinecraftLoginAsync(
        string msAccessToken, 
        string msRefreshToken, 
        CancellationToken cancellationToken)
    {
        // 1. Xbox Live User Authentication
        var xblRequest = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={msAccessToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var xblResponse = await _httpClient.PostAsJsonAsync("https://user.auth.xboxlive.com/user/authenticate", xblRequest, cancellationToken);
        xblResponse.EnsureSuccessStatusCode();
        using var xblDoc = await JsonDocument.ParseAsync(await xblResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        string xblToken = xblDoc.RootElement.GetProperty("Token").GetString()!;
        string userHash = xblDoc.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;

        // 2. XSTS Token Authentication
        var xstsRequest = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var xstsResponse = await _httpClient.PostAsJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", xstsRequest, cancellationToken);
        await EnsureSuccessWithDetailsAsync(xstsResponse, "Falha na autenticação XSTS", cancellationToken);
        using var xstsDoc = await JsonDocument.ParseAsync(await xstsResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        string xstsToken = xstsDoc.RootElement.GetProperty("Token").GetString()!;

        // 3. Minecraft Services Login
        var mcLoginRequest = new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        };

        using var mcResponse = await _httpClient.PostAsJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", mcLoginRequest, cancellationToken);
        mcResponse.EnsureSuccessStatusCode();
        using var mcDoc = await JsonDocument.ParseAsync(await mcResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        string mcToken = mcDoc.RootElement.GetProperty("access_token").GetString()!;
        int expiresIn = mcDoc.RootElement.GetProperty("expires_in").GetInt32();

        // 4. Perfil de Jogador Mojang
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);

        using var profileResponse = await _httpClient.SendAsync(profileRequest, cancellationToken);
        if (!profileResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Conta sem perfil Minecraft válido. Certifique-se de que a conta possui Minecraft (ou Game Pass) e já tenha entrado no jogo oficial pelo menos uma vez.");
        }
        using var profileDoc = await JsonDocument.ParseAsync(await profileResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        
        string rawId = profileDoc.RootElement.GetProperty("id").GetString()!;
        string username = profileDoc.RootElement.GetProperty("name").GetString()!;
        string formattedUuid = $"{rawId[..8]}-{rawId[8..12]}-{rawId[12..16]}-{rawId[16..20]}-{rawId[20..32]}";

        string vaultKey = $"account_token_{formattedUuid}";
        await _secureVault.StoreSecretAsync(vaultKey, msRefreshToken);

        return new AccountProfile
        {
            Id = formattedUuid,
            Username = username,
            Type = AccountType.Microsoft,
            VaultKey = vaultKey,
            CachedAccessToken = mcToken,
            TokenExpiration = DateTime.UtcNow.AddSeconds(expiresIn - 60)
        };
    }

    private static async Task EnsureSuccessWithDetailsAsync(HttpResponseMessage response, string context, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string detail = body.Length > 0 ? $" — {body[..Math.Min(body.Length, 200)]}" : string.Empty;
        throw new InvalidOperationException($"{context} (HTTP {(int)response.StatusCode}): {detail}");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
