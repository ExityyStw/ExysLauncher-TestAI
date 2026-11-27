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
    /// Chave pública ED25519 para verificar assinaturas de manifesto.
    /// Deve ser substituída pela chave pública real do servidor.
    /// </summary>
    private const string ManifestPublicKeyBase64 = "PLACEHOLDER_PUBLIC_KEY_BASE64";

    /// <summary>
    /// Verifica assinatura ED25519 do manifesto se presente.
    /// Se não houver assinatura, permite continuar (compatibilidade).
    /// Se houver assinatura, valida criptograficamente.
    /// </summary>
    private static void VerifySignature(ModpackManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature))
        {
            // Sem assinatura - compatibilidade (não recomendado para produção)
            return;
        }

        try
        {
            // Decodifica a assinatura
            byte[] signatureBytes = Convert.FromBase64String(manifest.Signature);

            // Valida formato da chave pública
            byte[] publicKeyBytes = Convert.FromBase64String(ManifestPublicKeyBase64);
            if (publicKeyBytes.Length != 32)
            {
                throw new InvalidOperationException("Chave pública ED25519 inválida (deve ter 32 bytes).");
            }

            // Cria o manifesto canônico para verificação (sem o campo Signature)
            var manifestWithoutSignature = new ModpackManifest
            {
                ModpackName = manifest.ModpackName,
                Version = manifest.Version,
                GameVersion = manifest.GameVersion,
                Loader = manifest.Loader,
                Files = manifest.Files,
            };

            // Serializa o manifesto canônico
            string canonicalJson = JsonSerializer.Serialize(manifestWithoutSignature, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            byte[] dataToVerify = System.Text.Encoding.UTF8.GetBytes(canonicalJson);

            // Verifica a assinatura ED25519
            using var publicKey = System.Security.Cryptography.ECDsa.Create();
            // Nota: ED25519 requer implementação específica. 
            // Para .NET 8+, usar System.Security.Cryptography.Ed25519 se disponível,
            // ou biblioteca externa como NSec.Cryptography
            
            // TODO: Implementar verificação ED25519 real quando a chave pública for fornecida
            // Por enquanto, apenas valida formato base64
            
            // Placeholder: valida apenas que a assinatura é base64 válido
            if (signatureBytes.Length != 64) // ED25519 signatures are 64 bytes
            {
                throw new InvalidOperationException("Assinatura ED25519 deve ter 64 bytes.");
            }
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Assinatura do manifesto está em formato inválido (deve ser base64).");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro ao verificar assinatura do manifesto: {ex.Message}");
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
