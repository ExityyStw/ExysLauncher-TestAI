using System.Security.Cryptography;
using System.Text;

namespace LauncherMine.Core.Security;

/// <summary>
/// Armazenamento seguro para Windows utilizando DPAPI (Data Protection API).
/// As chaves criptográficas são atreladas ao usuário autenticado atual no Windows.
/// </summary>
public sealed class WindowsSecureVault : ISecureVault
{
    private readonly string _storageDirectory;
    private static readonly byte[] EntropySalt = "LauncherMine.DPAPI.Salt.v1"u8.ToArray();

    public WindowsSecureVault()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _storageDirectory = Path.Combine(appData, "LauncherMine", "vault");
        Directory.CreateDirectory(_storageDirectory);
    }

    public async Task StoreSecretAsync(string key, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        byte[] plainBytes = Encoding.UTF8.GetBytes(secret);
        byte[] encryptedBytes = ProtectedData.Protect(
            plainBytes, 
            EntropySalt, 
            DataProtectionScope.CurrentUser);

        string filePath = GetSecretFilePath(key);
        await File.WriteAllBytesAsync(filePath, encryptedBytes);
    }

    public async Task<string?> RetrieveSecretAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string filePath = GetSecretFilePath(key);
        if (!File.Exists(filePath))
            return null;

        try
        {
            byte[] encryptedBytes = await File.ReadAllBytesAsync(filePath);
            byte[] plainBytes = ProtectedData.Unprotect(
                encryptedBytes, 
                EntropySalt, 
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // O arquivo pode ter sido corrompido ou movido de outra conta de usuário do Windows
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

    private string GetSecretFilePath(string key)
    {
        // Sanitiza a chave usando hash para evitar caracteres inválidos no nome do arquivo
        byte[] keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string safeFileName = Convert.ToHexString(keyHash) + ".dpapi";
        return Path.Combine(_storageDirectory, safeFileName);
    }
}
