using System.Security;
using System.Text.RegularExpressions;
using LauncherMine.Core.Auth;
using LauncherMine.Core.Models;

namespace LauncherMine.Tests;

public class OfflineAuthServiceTests
{
    [Theory]
    [InlineData("Steve")]
    [InlineData("Notch")]
    [InlineData("Player_123")]
    [InlineData("abc")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    public void CreateOfflineAccount_ValidUsername_Succeeds(string username)
    {
        var account = OfflineAuthService.CreateOfflineAccount(username);

        Assert.Equal(AccountType.Offline, account.Type);
        Assert.Equal(username, account.Username);
        Assert.Matches(new Regex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"), account.Id);
        Assert.Equal("OFFLINE_ACCESS_TOKEN", account.CachedAccessToken);
        Assert.Equal(DateTime.MaxValue, account.TokenExpiration);
        Assert.Null(account.VaultKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]           // curto demais (< 3)
    [InlineData("ABCDEFGHIJKLMNOPQ")] // longo demais (> 16)
    [InlineData("name with space")]
    [InlineData("name-with-dash")]
    [InlineData("name.dot")]
    [InlineData("name;drop table")]
    [InlineData("nomeçãocomacento")]
    public void CreateOfflineAccount_InvalidUsername_Throws(string username)
    {
        Assert.ThrowsAny<Exception>(() => OfflineAuthService.CreateOfflineAccount(username));
    }

    [Fact]
    public void CreateOfflineAccount_NullOrWhitespace_ThrowsArgument()
    {
        Assert.Throws<ArgumentException>(() => OfflineAuthService.CreateOfflineAccount("   "));
    }

    [Fact]
    public void GenerateMojangOfflineUuid_Notch_MatchesKnownVector()
    {
        // UUID offline do Notch: MD5("OfflinePlayer:Notch") com bits de versão/variante — verificado independentemente
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f",
            OfflineAuthService.GenerateMojangOfflineUuid("Notch"));
    }

    [Fact]
    public void GenerateMojangOfflineUuid_IsDeterministic()
    {
        string first = OfflineAuthService.GenerateMojangOfflineUuid("Steve");
        string second = OfflineAuthService.GenerateMojangOfflineUuid("Steve");

        Assert.Equal(first, second);
    }

    [Fact]
    public void GenerateMojangOfflineUuid_DifferentUsers_DifferentUuids()
    {
        Assert.NotEqual(
            OfflineAuthService.GenerateMojangOfflineUuid("Alice"),
            OfflineAuthService.GenerateMojangOfflineUuid("Bob"));
    }

    [Fact]
    public void GenerateMojangOfflineUuid_HasVersion3AndIetfVariant()
    {
        string uuid = OfflineAuthService.GenerateMojangOfflineUuid("Tester");
        var guid = Guid.Parse(uuid);

        // Versão 3 (name-based MD5): nibble alto do 3º grupo = 3
        Assert.Equal(3, (guid.ToByteArray()[7] >> 4));
        // Variante IETF: bits 10xx no 1º byte do 4º grupo
        Assert.True((guid.ToByteArray()[8] & 0xC0) == 0x80);
    }

    [Fact]
    public void CreateOfflineAccount_TrimsWhitespace()
    {
        var account = OfflineAuthService.CreateOfflineAccount("  Steve  ");

        Assert.Equal("Steve", account.Username);
    }
}
