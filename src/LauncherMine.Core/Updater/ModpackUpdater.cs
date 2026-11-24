using System.Collections.Concurrent;
using System.Security;
using System.Security.Cryptography;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;

namespace LauncherMine.Core.Updater;

/// <summary>
/// Motor de sincronização de modpacks focado em alta velocidade, integridade SHA-256 e anti-tampering.
/// </summary>
public sealed class ModpackUpdater : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly int _maxConcurrentDownloads;

    public ModpackUpdater(int maxConcurrentDownloads = 8)
    {
        _maxConcurrentDownloads = maxConcurrentDownloads;

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LauncherMine-Updater/1.0");
    }

    /// <summary>
    /// Sincroniza a instância com o manifesto: valida hashes, baixa faltantes e remove órfãos.
    /// </summary>
    public async Task SynchronizeAsync(
        string instanceDirectory,
        ModpackManifest manifest,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        string modsDirectory = PathSecurity.ResolveSecurePath(instanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);

        var expectedPaths = new HashSet<string>(OperatingSystem.IsWindows() 
            ? StringComparer.OrdinalIgnoreCase 
            : StringComparer.Ordinal);

        var filesToDownload = new List<ManifestFileEntry>();
        long totalBytesToDownload = 0;
        int totalFiles = manifest.Files.Count;
        int processedFiles = 0;

        // 1. Verificação prévia de arquivos existentes
        foreach (var entry in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Validação HTTPS mandatória
            if (!Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new SecurityException($"Insegurança de Rede: O arquivo '{entry.RelativePath}' requer protocolo HTTPS ({entry.DownloadUrl}).");
            }

            string secureLocalPath = PathSecurity.ResolveSecurePath(instanceDirectory, entry.RelativePath);
            expectedPaths.Add(secureLocalPath);

            if (File.Exists(secureLocalPath))
            {
                string localHash = await ComputeFileSha256Async(secureLocalPath, cancellationToken);
                if (string.Equals(localHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    processedFiles++;
                    progress?.Report(new UpdateProgress(totalFiles, processedFiles, totalBytesToDownload, 0, Path.GetFileName(secureLocalPath)));
                    continue;
                }
            }

            filesToDownload.Add(entry);
            totalBytesToDownload += entry.Size;
        }

        // 2. Download Paralelo com Staging e Hash em Streaming
        using var semaphore = new SemaphoreSlim(_maxConcurrentDownloads);
        long downloadedBytes = 0;

        var downloadTasks = filesToDownload.Select(async entry =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                string secureLocalPath = PathSecurity.ResolveSecurePath(instanceDirectory, entry.RelativePath);
                
                await DownloadAndVerifyFileAsync(
                    entry, 
                    secureLocalPath, 
                    bytesRead => 
                    {
                        long currentTotal = Interlocked.Add(ref downloadedBytes, bytesRead);
                        progress?.Report(new UpdateProgress(
                            totalFiles, 
                            Volatile.Read(ref processedFiles), 
                            totalBytesToDownload, 
                            currentTotal, 
                            Path.GetFileName(secureLocalPath)));
                    }, 
                    cancellationToken);

                int completed = Interlocked.Increment(ref processedFiles);
                progress?.Report(new UpdateProgress(
                    totalFiles, 
                    completed, 
                    totalBytesToDownload, 
                    Interlocked.Read(ref downloadedBytes), 
                    Path.GetFileName(secureLocalPath)));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(downloadTasks);

        // 3. Limpeza Automática de Mods Órfãos
        CleanupOrphanFiles(modsDirectory, expectedPaths);
    }

    private async Task DownloadAndVerifyFileAsync(
        ManifestFileEntry entry,
        string destinationPath,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        string tempFilePath = destinationPath + ".downloading.tmp";

        try
        {
            using (var response = await _httpClient.GetAsync(entry.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(
                    tempFilePath, 
                    FileMode.Create, 
                    FileAccess.ReadWrite, 
                    FileShare.None, 
                    bufferSize: 81920, 
                    useAsync: true);

                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    sha256.AppendData(buffer, 0, bytesRead);
                    onBytesRead(bytesRead);
                }

                fileStream.Flush(flushToDisk: true);

                byte[] hashBytes = sha256.GetHashAndReset();
                string calculatedHash = Convert.ToHexString(hashBytes);

                if (!string.Equals(calculatedHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CryptographicException(
                        $"Violação de integridade no arquivo '{entry.RelativePath}'. " +
                        $"Esperado: {entry.Sha256}, Obtido: {calculatedHash}. Possível adulteração de rede detectada.");
                }
            }

            File.Move(tempFilePath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { /* Ignore */ }
            }
        }
    }

    private static void CleanupOrphanFiles(string modsDirectory, HashSet<string> validPaths)
    {
        if (!Directory.Exists(modsDirectory)) return;

        var localJars = Directory.EnumerateFiles(modsDirectory, "*.jar", SearchOption.AllDirectories);

        foreach (var localJar in localJars)
        {
            string canonicalLocal = Path.GetFullPath(localJar);
            if (!validPaths.Contains(canonicalLocal))
            {
                try
                {
                    File.Delete(canonicalLocal);
                }
                catch (IOException)
                {
                    // Ignora caso esteja em uso ou bloqueado
                }
            }
        }
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var sha256 = SHA256.Create();
        byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
