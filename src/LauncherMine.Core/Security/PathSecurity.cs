using System.Security;

namespace LauncherMine.Core.Security;

/// <summary>
/// Provedor de resolução canônica e segura de caminhos de arquivos.
/// Protege contra explorações de Path Traversal (CWE-22, CWE-23), injeção de caracteres nulos,
/// referências relativas anômalas e travessia de fronteiras de instâncias.
/// </summary>
public static class PathSecurity
{
    private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

    /// <summary>
    /// Resolve um caminho relativo estritamente contido dentro do diretório base da instância.
    /// Lança exceção de segurança se o caminho tentar escapar do diretório base.
    /// </summary>
    public static string ResolveSecurePath(string baseDirectory, string untrustedRelativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("O diretório base não pode ser nulo ou vazio.", nameof(baseDirectory));

        if (string.IsNullOrWhiteSpace(untrustedRelativePath))
            throw new ArgumentException("O caminho relativo não pode ser vazio.", nameof(untrustedRelativePath));

        // Bloqueia caracteres nulos ou inválidos do SO
        if (untrustedRelativePath.IndexOfAny(InvalidPathChars) >= 0 || untrustedRelativePath.Contains('\0'))
            throw new SecurityException($"Caminho contém caracteres ilegais de sistema: '{untrustedRelativePath}'");

        // Bloqueia caminhos absolutos no manifesto
        if (Path.IsPathRooted(untrustedRelativePath))
            throw new SecurityException($"Caminhos absolutos não são autorizados no manifesto: '{untrustedRelativePath}'");

        // Normalização agnóstica de separadores de diretório
        string sanitizedRelative = untrustedRelativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        // Obtém caminhos canônicos absolutos
        string canonicalBase = Path.GetFullPath(baseDirectory);
        if (!canonicalBase.EndsWith(Path.DirectorySeparatorChar))
            canonicalBase += Path.DirectorySeparatorChar;

        string targetFullPath = Path.GetFullPath(Path.Combine(canonicalBase, sanitizedRelative));

        // Validação estrita de fronteira (Boundary Check)
        StringComparison comparison = OperatingSystem.IsWindows() 
            ? StringComparison.OrdinalIgnoreCase 
            : StringComparison.Ordinal;

        if (!targetFullPath.StartsWith(canonicalBase, comparison))
        {
            throw new SecurityException(
                $"Violação Crítica de Path Traversal: Tentativa de acessar local fora do diretório da instância: '{untrustedRelativePath}'");
        }

        return targetFullPath;
    }
}
