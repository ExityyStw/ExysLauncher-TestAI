namespace LauncherMine.Core.Security;

/// <summary>
/// Contrato para armazenamento de credenciais e tokens com criptografia nativa do SO.
/// </summary>
public interface ISecureVault
{
    Task StoreSecretAsync(string key, string secret);
    Task<string?> RetrieveSecretAsync(string key);
    Task DeleteSecretAsync(string key);
}
