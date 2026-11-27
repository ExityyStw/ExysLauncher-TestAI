using System.Security;
using LauncherMine.Core.Security;

namespace LauncherMine.Tests;

public class PathSecurityTests
{
    private static string BaseDir => Path.Combine(Path.GetTempPath(), "launchermine-test-base");

    [Fact]
    public void ResolveSecurePath_ValidRelativePath_StaysInsideBase()
    {
        string result = PathSecurity.ResolveSecurePath(BaseDir, "mods/jei.jar");

        Assert.StartsWith(Path.GetFullPath(BaseDir), result);
        Assert.EndsWith(Path.Combine("mods", "jei.jar"), result);
    }

    [Fact]
    public void ResolveSecurePath_ForwardSlashes_AreNormalized()
    {
        string result = PathSecurity.ResolveSecurePath(BaseDir, "config/options.txt");

        Assert.DoesNotContain('/', result);
        Assert.EndsWith("options.txt", result);
    }

    [Fact]
    public void ResolveSecurePath_ParentTraversal_Throws()
    {
        Assert.Throws<SecurityException>(() =>
            PathSecurity.ResolveSecurePath(BaseDir, "../outside.txt"));
    }

    [Fact]
    public void ResolveSecurePath_DeepTraversal_Throws()
    {
        Assert.Throws<SecurityException>(() =>
            PathSecurity.ResolveSecurePath(BaseDir, "mods/../../etc/passwd"));
    }

    [Fact]
    public void ResolveSecurePath_AbsolutePath_Throws()
    {
        string absolute = Path.Combine(Path.GetTempPath(), "evil.txt");
        Assert.Throws<SecurityException>(() =>
            PathSecurity.ResolveSecurePath(BaseDir, absolute));
    }

    [Fact]
    public void ResolveSecurePath_RootedUnixStyle_Throws()
    {
        Assert.Throws<SecurityException>(() =>
            PathSecurity.ResolveSecurePath(BaseDir, "/etc/passwd"));
    }

    [Fact]
    public void ResolveSecurePath_NullByte_Throws()
    {
        Assert.Throws<SecurityException>(() =>
            PathSecurity.ResolveSecurePath(BaseDir, "mods/\0evil.jar"));
    }

    [Fact]
    public void ResolveSecurePath_EmptyPath_ThrowsArgument()
    {
        Assert.Throws<ArgumentException>(() =>
            PathSecurity.ResolveSecurePath(BaseDir, "  "));
    }

    [Fact]
    public void ResolveSecurePath_EmptyBase_ThrowsArgument()
    {
        Assert.Throws<ArgumentException>(() =>
            PathSecurity.ResolveSecurePath("", "mods/x.jar"));
    }

    [Fact]
    public void ResolveSecurePath_InternalDotDot_ThatStaysInside_IsAllowed()
    {
        // "a/../b" normaliza para "b" — ainda dentro da base, deve passar
        string result = PathSecurity.ResolveSecurePath(BaseDir, "a/../b/file.txt");

        Assert.EndsWith(Path.Combine("b", "file.txt"), result);
    }

    [Fact]
    public void ResolveSecurePath_SiblingPrefixAttack_Throws()
    {
        // Base "foo" não deve aceitar "foobar/x" — boundary check cobre isso
        string trickyBase = Path.Combine(Path.GetTempPath(), "launchermine-base");
        Assert.Throws<SecurityException>(() =>
            PathSecurity.ResolveSecurePath(trickyBase, "../launchermine-base-evil/x.txt"));
    }
}
