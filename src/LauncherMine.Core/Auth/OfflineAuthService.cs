using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LauncherMine.Core.Models;

namespace LauncherMine.Core.Auth;

/// <summary>
/// Serviço de autenticação para contas offline com validação rigorosa anti-injeção
/// e geração do UUID v3 compatível com o padrão histórico da Mojang.
/// </summary>
public static partial class OfflineAuthService
{
    // Restringe nomes de 3 a 16 caracteres alfanuméricos e underscore (padrão oficial Mojang)
    [GeneratedRegex("^[a-zA-Z0-9_]{3,16}$", RegexOptions.Compiled)]
    private static partial Regex ValidUsernameRegex();

    public static AccountProfile CreateOfflineAccount(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("O nome de usuário não pode ser vazio.", nameof(username));

        string trimmedName = username.Trim();

        if (!ValidUsernameRegex().IsMatch(trimmedName))
        {
            throw new SecurityException(
                $"Nome de usuário inválido: '{trimmedName}'. O nome deve conter entre 3 e 16 caracteres (letras, números e sublinhados), sem espaços ou caracteres especiais.");
        }

        string offlineUuid = GenerateMojangOfflineUuid(trimmedName);

        return new AccountProfile
        {
            Id = offlineUuid,
            Username = trimmedName,
            Type = AccountType.Offline,
            VaultKey = null,
            CachedAccessToken = "OFFLINE_ACCESS_TOKEN",
            TokenExpiration = DateTime.MaxValue
        };
    }

    /// <summary>
    /// Gera o UUID v3 baseado no MD5 de 'OfflinePlayer:' + username, exatamente como o servidor vanilla faz.
    /// Java equivalente: UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(StandardCharsets.UTF_8))
    /// </summary>
    public static string GenerateMojangOfflineUuid(string username)
    {
        byte[] input = Encoding.UTF8.GetBytes($"OfflinePlayer:{username}");
        byte[] hash = MD5.HashData(input);

        // Define versão 3 (Name-based MD5)
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        // Define variante IETF (RFC 4122)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        string hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }
}
