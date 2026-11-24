using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LauncherMine.Core.Launch;

public sealed record JavaInstallation(
    string Path,
    int MajorVersion,
    string Vendor,
    bool Is64Bit
);

/// <summary>
/// Detector cross-platform de instalações Java seguras (Windows e Linux).
/// Realiza introspecção de versões sem expor shell a comandos maliciosos.
/// </summary>
public static partial class JavaDetector
{
    [GeneratedRegex(@"(?:version|openjdk version)\s+""(\d+)(?:\.(\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VersionRegex();

    public static List<JavaInstallation> FindAvailableJavaInstallations()
    {
        var found = new List<JavaInstallation>();
        var candidates = GetCandidateJavaPaths();

        foreach (var javaBin in candidates)
        {
            if (!File.Exists(javaBin)) continue;

            try
            {
                var info = ProbeJavaBinary(javaBin);
                if (info != null && !found.Any(f => string.Equals(f.Path, info.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(info);
                }
            }
            catch
            {
                // Ignora binários defeituosos ou sem permissão
            }
        }

        return found.OrderByDescending(j => j.MajorVersion).ToList();
    }

    /// <summary>
    /// Seleciona a instalação de Java ideal para a versão do Minecraft indicada.
    /// MC 1.20.6+ e 1.21+ exigem estritamente Java 21+.
    /// </summary>
    public static JavaInstallation? GetRecommendedJava(string mcVersion, List<JavaInstallation> available)
    {
        int requiredMajor = 21; // Padrão moderno (1.20.6 / 1.21.1)

        if (Version.TryParse(mcVersion, out var ver))
        {
            if (ver < new Version(1, 17))
                requiredMajor = 8;
            else if (ver < new Version(1, 18))
                requiredMajor = 16;
            else if (ver < new Version(1, 20, 6))
                requiredMajor = 17;
            else
                requiredMajor = 21;
        }

        // Tenta achar versão exata ou compatível mais próxima
        return available.FirstOrDefault(j => j.MajorVersion == requiredMajor)
               ?? available.FirstOrDefault(j => j.MajorVersion >= requiredMajor);
    }

    private static List<string> GetCandidateJavaPaths()
    {
        var paths = new HashSet<string>(OperatingSystem.IsWindows() 
            ? StringComparer.OrdinalIgnoreCase 
            : StringComparer.Ordinal);

        // 1. PATH do Sistema
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            char separator = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (var segment in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(segment, OperatingSystem.IsWindows() ? "java.exe" : "java");
                if (File.Exists(candidate))
                    paths.Add(Path.GetFullPath(candidate));
            }
        }

        // 2. JAVA_HOME
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            string candidate = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(candidate))
                paths.Add(Path.GetFullPath(candidate));
        }

        // 3. Diretórios Padrões de Sistema Operacional
        if (OperatingSystem.IsWindows())
        {
            string[] searchRoots = [
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Java",
                @"C:\Program Files\Microsoft",
                @"C:\Program Files\BellSoft",
                @"C:\Program Files\Zulu"
            ];

            foreach (var root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string candidate = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(candidate))
                        paths.Add(Path.GetFullPath(candidate));
                }
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            string[] linuxRoots = [
                "/usr/lib/jvm",
                "/usr/java",
                "/opt/jdk"
            ];

            foreach (var root in linuxRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string candidate = Path.Combine(dir, "bin", "java");
                    if (File.Exists(candidate))
                        paths.Add(Path.GetFullPath(candidate));
                }
            }
        }

        return paths.ToList();
    }

    private static JavaInstallation? ProbeJavaBinary(string javaPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Passa o argumento diretamente sem invocar shell (Zero Command Injection)
        psi.ArgumentList.Add("-version");

        using var process = Process.Start(psi);
        if (process == null) return null;

        string output = process.StandardError.ReadToEnd(); // java -version emite em stderr
        process.WaitForExit(3000);

        var match = VersionRegex().Match(output);
        if (!match.Success) return null;

        int major = int.Parse(match.Groups[1].Value);
        // Suporte a formatos antigos como "1.8.0"
        if (major == 1 && match.Groups[2].Success)
        {
            major = int.Parse(match.Groups[2].Value);
        }

        bool is64 = output.Contains("64-Bit", StringComparison.OrdinalIgnoreCase);
        string vendor = output.Contains("Adoptium", StringComparison.OrdinalIgnoreCase) || output.Contains("Temurin", StringComparison.OrdinalIgnoreCase) ? "Eclipse Adoptium (Temurin)"
            : output.Contains("Zulu", StringComparison.OrdinalIgnoreCase) ? "Azul Zulu"
            : output.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? "Microsoft"
            : "OpenJDK / Java";

        return new JavaInstallation(Path.GetFullPath(javaPath), major, vendor, is64);
    }
}
