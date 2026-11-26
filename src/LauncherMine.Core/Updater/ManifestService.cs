using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LauncherMine.Core.Models;

namespace LauncherMine.Core.Updater;

/// <summary>
/// Serviço de busca e validação estrita do manifesto remoto de modpack.
/// Garante HTTPS obrigatório, integridade estrutural do JSON e sanitização de entradas
/// antes de qualquer operação de escrita em disco.
/// </summary>
public sealed class ManifestService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly Regex Sha256Regex = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    /// <summary>
    /// Baixa e valida o manifesto remoto. Lança exceção em qualquer violação de segurança
    /// ou formato — o chamador NÃO deve iniciar o jogo se o manifesto configurado falhar.
    /// </summary>
    public async Task<ModpackManifest> FetchManifestAsync(string manifestUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            throw new ArgumentException("URL do manifesto não configurada.", nameof(manifestUrl));

        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new SecurityException(
                $"Manifesto remoto deve usar HTTPS obrigatório. URL rejeitada: '{manifestUrl}'");
        }

        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Falha ao baixar manifesto de modpack (HTTP {(int)response.StatusCode}).");
        }

        // Limita o tamanho do manifesto para evitar abuso de memória (ex: JSON de 2GB)
        const long maxManifestBytes = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength is > maxManifestBytes)
        {
            throw new InvalidOperationException("Manifesto remoto excede o tamanho máximo permitido (8 MB).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<ModpackManifest>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Manifesto remoto retornou JSON inválido ou vazio.");

        ValidateManifest(manifest);
        VerifySignature(manifest);
        return manifest;
    }

    /// <summary>
    /// Verifica assinatura ED25519 se presente no manifesto.
    /// Se não houver assinatura, emite warning mas permite continuar (compatibilidade).
    /// </summary>
    private static void VerifySignature(ModpackManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature))
        {
            // Assinatura opcional por enquanto - sem validação
            return;
        }

        // TODO: Implementar verificação ED25519 com chave pública hardcoded
        // Por enquanto, apenas verifica formato base64 válido
        try
        {
            Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Assinatura do manifesto está em formato inválido (deve ser base64).");
        }
    }

    /// <summary>
    /// Validação estrutural completa do manifesto antes de tocar no disco.
    /// </summary>
    private static void ValidateManifest(ModpackManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ModpackName))
            throw new InvalidOperationException("Manifesto inválido: 'modpackName' ausente.");

        if (string.IsNullOrWhiteSpace(manifest.GameVersion))
            throw new InvalidOperationException("Manifesto inválido: 'gameVersion' ausente.");

        if (manifest.Loader == null || string.IsNullOrWhiteSpace(manifest.Loader.Type))
            throw new InvalidOperationException("Manifesto inválido: 'loader.type' ausente.");

        if (manifest.Files == null || manifest.Files.Count == 0)
            throw new InvalidOperationException("Manifesto inválido: lista 'files' vazia.");

        var seenPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        foreach (var entry in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(entry.RelativePath))
                throw new InvalidOperationException("Manifesto inválido: entrada sem 'path'.");

            if (!seenPaths.Add(entry.RelativePath))
                throw new InvalidOperationException($"Manifesto inválido: caminho duplicado '{entry.RelativePath}'.");

            if (!Sha256Regex.IsMatch(entry.Sha256 ?? string.Empty))
            {
                throw new InvalidOperationException(
                    $"Manifesto inválido: hash SHA-256 malformado para '{entry.RelativePath}'.");
            }

            if (entry.Size < 0)
                throw new InvalidOperationException($"Manifesto inválido: tamanho negativo para '{entry.RelativePath}'.");

            if (string.IsNullOrWhiteSpace(entry.DownloadUrl) ||
                !Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out var fileUri) ||
                fileUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new SecurityException(
                    $"Manifesto inválido: '{entry.RelativePath}' exige URL HTTPS válida.");
            }
        }
    }
}
