using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LauncherMine.Core.Security;

/// <summary>
/// Armazenamento seguro para sistemas Linux / Unix.
/// Implementa persistência isolada com permissões de arquivo estritas (POSIX 0600: somente leitura/escrita do proprietário),
/// associado a uma chave derivada por máquina e usuário com criptografia AES-GCM 256 bits.
/// </summary>
public sealed class LinuxSecureVault : ISecureVault
{
    private readonly string _storageDirectory;
    private readonly byte[] _masterKey;

    public LinuxSecureVault()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _storageDirectory = Path.Combine(home, ".local", "share", "LauncherMine", "vault");
        Directory.CreateDirectory(_storageDirectory);

        // Define permissão do diretório como 0700 no Linux
        SetUnixFilePermissions(_storageDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _masterKey = DeriveMachineUserKey();
    }

    public async Task StoreSecretAsync(string key, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        byte[] plainBytes = Encoding.UTF8.GetBytes(secret);

        // AES-256-GCM com Nonce e Tag de Autenticação
        byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plainBytes.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using (var aesGcm = new AesGcm(_masterKey, AesGcm.TagByteSizes.MaxSize))
        {
            aesGcm.Encrypt(nonce, plainBytes, ciphertext, tag);
        }

        // Formato binário: [Nonce (12)] + [Tag (16)] + [Ciphertext (N)]
        byte[] combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

        string filePath = GetSecretFilePath(key);
        await File.WriteAllBytesAsync(filePath, combined);

        // Define permissões estritas 0600 (apenas o próprio usuário tem permissão)
        SetUnixFilePermissions(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public async Task<string?> RetrieveSecretAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string filePath = GetSecretFilePath(key);
        if (!File.Exists(filePath))
            return null;

        try
        {
            byte[] combined = await File.ReadAllBytesAsync(filePath);
            if (combined.Length < AesGcm.NonceByteSizes.MaxSize + AesGcm.TagByteSizes.MaxSize)
                return null;

            int nonceSize = AesGcm.NonceByteSizes.MaxSize;
            int tagSize = AesGcm.TagByteSizes.MaxSize;
            int ciphertextSize = combined.Length - nonceSize - tagSize;

            byte[] nonce = combined[..nonceSize];
            byte[] tag = combined[nonceSize..(nonceSize + tagSize)];
            byte[] ciphertext = combined[(nonceSize + tagSize)..];

            byte[] decrypted = new byte[ciphertextSize];
            using (var aesGcm = new AesGcm(_masterKey, AesGcm.TagByteSizes.MaxSize))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, decrypted);
            }

            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public Task DeleteSecretAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string filePath = GetSecretFilePath(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    private static void SetUnixFilePermissions(string path, UnixFileMode modes)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                File.SetUnixFileMode(path, modes);
            }
            catch
            {
                // Ignora se o sistema de arquivos não der suporte nativo a POSIX ACLs
            }
        }
    }

    private static byte[] DeriveMachineUserKey()
    {
        // Obtém entropia estável de identificação de máquina do Linux (/etc/machine-id)
        string machineId = string.Empty;
        if (File.Exists("/etc/machine-id"))
        {
            machineId = File.ReadAllText("/etc/machine-id").Trim();
        }
        else if (File.Exists("/var/lib/dbus/machine-id"))
        {
            machineId = File.ReadAllText("/var/lib/dbus/machine-id").Trim();
        }

        string user = Environment.UserName;
        string combinedSeed = $"LauncherMine:{machineId}:{user}:Salt_9981";

        return SHA256.HashData(Encoding.UTF8.GetBytes(combinedSeed));
    }

    private string GetSecretFilePath(string key)
    {
        byte[] keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string safeFileName = Convert.ToHexString(keyHash) + ".vault";
        return Path.Combine(_storageDirectory, safeFileName);
    }
}
