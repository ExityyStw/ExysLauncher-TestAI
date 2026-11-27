using System.Text.Json;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;

namespace LauncherMine.Core.Auth;

/// <summary>
/// Gerenciador central de contas.
/// Mantém perfis e comuta a conta ativa de forma transparente para a UI.
/// </summary>
public sealed class AccountManager
{
    private readonly string _storageFilePath;
    private readonly ISecureVault _secureVault;
    private readonly MicrosoftAuthService _microsoftAuthService;
    private readonly List<AccountProfile> _accounts = [];
    private readonly SemaphoreSlim _lock = new(1, 1); // Lock para operações assíncronas

    public IReadOnlyList<AccountProfile> Accounts => _accounts.AsReadOnly();
    public AccountProfile? ActiveAccount { get; private set; }

    public event Action? AccountsChanged;
    public event Action<AccountProfile?>? ActiveAccountChanged;

    public AccountManager(ISecureVault secureVault)
    {
        _secureVault = secureVault;
        _microsoftAuthService = new MicrosoftAuthService(_secureVault);

        string basePath = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        string appDir = Path.Combine(basePath, "LauncherMine");
        Directory.CreateDirectory(appDir);
        _storageFilePath = Path.Combine(appDir, "accounts.json");
    }

    public async Task LoadAccountsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _accounts.Clear();

            if (File.Exists(_storageFilePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_storageFilePath);
                    var loaded = JsonSerializer.Deserialize<List<AccountProfile>>(json);
                    if (loaded != null)
                    {
                        _accounts.AddRange(loaded);
                    }
                }
                catch
                {
                    // Em caso de falha de leitura, preserva lista limpa
                }
            }

            // Seleciona a conta mais recente como ativa
            ActiveAccount = _accounts.OrderByDescending(a => a.LastUsed).FirstOrDefault();
            AccountsChanged?.Invoke();
            ActiveAccountChanged?.Invoke(ActiveAccount);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAccountsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await SaveAccountsCoreAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    // Núcleo do save SEM lock — chamado por quem já detém _lock
    private async Task SaveAccountsCoreAsync()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(_accounts, options);
        await File.WriteAllTextAsync(_storageFilePath, json);
        AccountsChanged?.Invoke();
    }

    public async Task AddOrUpdateAccountAsync(AccountProfile account)
    {
        await _lock.WaitAsync();
        try
        {
            int existingIndex = _accounts.FindIndex(a => a.Id == account.Id);
            if (existingIndex >= 0)
            {
                _accounts[existingIndex] = account;
            }
            else
            {
                _accounts.Add(account);
            }

            ActiveAccount = account;
            account.LastUsed = DateTime.UtcNow;

            await SaveAccountsCoreAsync();
            ActiveAccountChanged?.Invoke(ActiveAccount);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void SelectActiveAccount(string accountId)
    {
        _lock.Wait();
        try
        {
            var found = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (found != null)
            {
                ActiveAccount = found;
                found.LastUsed = DateTime.UtcNow;
                _ = SaveAccountsAsync();
                ActiveAccountChanged?.Invoke(ActiveAccount);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAccountAsync(string accountId)
    {
        await _lock.WaitAsync();
        try
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return;

            if (!string.IsNullOrEmpty(account.VaultKey))
            {
                await _secureVault.DeleteSecretAsync(account.VaultKey);
            }

            _accounts.Remove(account);

            if (ActiveAccount?.Id == accountId)
            {
                ActiveAccount = _accounts.OrderByDescending(a => a.LastUsed).FirstOrDefault();
                ActiveAccountChanged?.Invoke(ActiveAccount);
            }

            await SaveAccountsCoreAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Obtém um token de sessão válido, renovando via refresh token se for Microsoft e estiver expirado.
    /// </summary>
    public async Task<string> GetValidAccessTokenAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        if (profile.Type == AccountType.Offline)
        {
            return "OFFLINE_ACCESS_TOKEN";
        }

        if (profile.IsTokenValid)
        {
            return profile.CachedAccessToken!;
        }

        return await _microsoftAuthService.RefreshAccessTokenAsync(profile, cancellationToken);
    }
}
