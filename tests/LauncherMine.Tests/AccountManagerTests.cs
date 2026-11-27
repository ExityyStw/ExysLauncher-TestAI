using LauncherMine.Core.Auth;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;

namespace LauncherMine.Tests;

/// <summary>
/// Regressão: AddOrUpdateAccountAsync/RemoveAccountAsync não podem deadlockar
/// ao salvar (SemaphoreSlim não é reentrante — bug real corrigido).
/// </summary>
public class AccountManagerTests
{
    private sealed class FakeVault : ISecureVault
    {
        private readonly Dictionary<string, string> _store = new();
        public Task StoreSecretAsync(string key, string secret) { _store[key] = secret; return Task.CompletedTask; }
        public Task<string?> RetrieveSecretAsync(string key) => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
        public Task DeleteSecretAsync(string key) { _store.Remove(key); return Task.CompletedTask; }
    }

    private static AccountProfile Offline(string name) =>
        OfflineAuthService.CreateOfflineAccount(name);

    [Fact]
    public async Task AddOrUpdateAccountAsync_CompletesWithoutDeadlock()
    {
        var mgr = new AccountManager(new FakeVault());
        var account = Offline("Tester");

        var task = mgr.AddOrUpdateAccountAsync(account);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(task, completed);
        Assert.Contains(account, mgr.Accounts);
        Assert.Equal(account, mgr.ActiveAccount);
    }

    [Fact]
    public async Task RemoveAccountAsync_CompletesWithoutDeadlock()
    {
        var mgr = new AccountManager(new FakeVault());
        var account = Offline("Tester");
        await mgr.AddOrUpdateAccountAsync(account);

        var task = mgr.RemoveAccountAsync(account.Id);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(task, completed);
        Assert.DoesNotContain(account, mgr.Accounts);
        Assert.Null(mgr.ActiveAccount);
    }

    [Fact]
    public async Task AccountsChanged_FiresOnAdd()
    {
        var mgr = new AccountManager(new FakeVault());
        int changedCount = 0;
        mgr.AccountsChanged += () => changedCount++;

        await mgr.AddOrUpdateAccountAsync(Offline("Steve"));

        Assert.True(changedCount > 0, "AccountsChanged deveria ter disparado ao adicionar conta");
    }

    [Fact]
    public async Task ActiveAccountChanged_FiresOnAdd()
    {
        var mgr = new AccountManager(new FakeVault());
        AccountProfile? active = null;
        mgr.ActiveAccountChanged += a => active = a;

        var account = Offline("Alex");
        await mgr.AddOrUpdateAccountAsync(account);

        Assert.Equal(account, active);
    }

    [Fact]
    public async Task ConcurrentAdds_AreSerialized()
    {
        var mgr = new AccountManager(new FakeVault());

        await Task.WhenAll(
            mgr.AddOrUpdateAccountAsync(Offline("UserOne")),
            mgr.AddOrUpdateAccountAsync(Offline("UserTwo")),
            mgr.AddOrUpdateAccountAsync(Offline("UserThree")));

        Assert.Equal(3, mgr.Accounts.Count);
        Assert.NotNull(mgr.ActiveAccount);
    }
}
