namespace LauncherMine.Core.Security;

/// <summary>
/// Fábrica de cofre seguro com resolução automática de SO.
/// </summary>
public static class SecureVaultFactory
{
    public static ISecureVault Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSecureVault();
        }
        
        return new LinuxSecureVault();
    }
}
