using System.Collections.Concurrent;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;

namespace LauncherMine.Core.Updater;

/// <summary>
/// Motor de sincronização de modpacks focado em alta velocidade, integridade SHA-256 e anti-tampering.
/// Mantém cache de hashes por instância para evitar revalidação completa a cada launch.
/// </summary>
public sealed class ModpackUpdater : IDisposable
{
    private const string StateFileName = ".exys-modpack-state.json";

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

        var hashCache = LoadHashCache(instanceDirectory);
        var newCache = new Dictionary<string, HashCacheEntry>(StringComparer.Ordinal);

        var expectedPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        var filesToDownload = new List<ManifestFileEntry>();
        long totalBytesToDownload = 0;
        int totalFiles = manifest.Files.Count;
        int processedFiles = 0;

        // 1. Verificação prévia de arquivos existentes (com cache de hash)
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
            string cacheKey = NormalizeCacheKey(entry.RelativePath);

            if (File.Exists(secureLocalPath))
            {
                var fileInfo = new FileInfo(secureLocalPath);

                // Fast path: tamanho confere E hash cacheado confere E arquivo não foi modificado desde o cache
                if (fileInfo.Length == entry.Size &&
                    hashCache.TryGetValue(cacheKey, out var cached) &&
                    string.Equals(cached.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase) &&
                    cached.FileLength == fileInfo.Length &&
                    cached.LastWriteUtcTicks == fileInfo.LastWriteTimeUtc.Ticks)
                {
                    newCache[cacheKey] = cached;
                    processedFiles++;
                    progress?.Report(new UpdateProgress(totalFiles, processedFiles, totalBytesToDownload, 0, Path.GetFileName(secureLocalPath)));
                    continue;
                }

                // Slow path: revalida o hash real apenas se o arquivo mudou ou não está em cache
                string localHash = await ComputeFileSha256Async(secureLocalPath, cancellationToken);
                if (string.Equals(localHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    newCache[cacheKey] = new HashCacheEntry(
                        entry.Sha256,
                        fileInfo.Length,
                        fileInfo.LastWriteTimeUtc.Ticks);
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
        var downloadLock = new object();

        var downloadTasks = filesToDownload.Select(async entry =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                string secureLocalPath = PathSecurity.ResolveSecurePath(instanceDirectory, entry.RelativePath);
                string cacheKey = NormalizeCacheKey(entry.RelativePath);

                try
                {
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
                }
                catch (Exception ex) when (entry.IsOptional && ex is not OperationCanceledException)
                {
                    // Arquivo opcional falhou: mantém eventual cópia local existente sem abortar a sync
                    if (File.Exists(secureLocalPath))
                    {
                        var fi = new FileInfo(secureLocalPath);
                        newCache[cacheKey] = new HashCacheEntry(entry.Sha256, fi.Length, fi.LastWriteTimeUtc.Ticks);
                    }
                    Interlocked.Increment(ref processedFiles);
                    return;
                }

                var verifiedInfo = new FileInfo(secureLocalPath);
                lock (downloadLock)
                {
                    newCache[cacheKey] = new HashCacheEntry(
                        entry.Sha256,
                        verifiedInfo.Length,
                        verifiedInfo.LastWriteTimeUtc.Ticks);
                }

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

        // 3. Persiste o cache de hashes e limpa entradas obsoletas
        SaveHashCache(instanceDirectory, newCache);

        // 4. Limpeza Automática de Mods Órfãos
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
                long totalRead = 0;

                while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    sha256.AppendData(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    onBytesRead(bytesRead);
                }

                fileStream.Flush(flushToDisk: true);

                // Validação de tamanho declarado no manifesto
                if (entry.Size > 0 && totalRead != entry.Size)
                {
                    throw new CryptographicException(
                        $"Tamanho divergente em '{entry.RelativePath}': manifesto declara {entry.Size} bytes, " +
                        $"download obteve {totalRead} bytes. Arquivo descartado.");
                }

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

    // ===================== Cache de Hashes por Instância =====================

    private sealed record HashCacheEntry(string Sha256, long FileLength, long LastWriteUtcTicks);

    private static string GetStateFilePath(string instanceDirectory)
        => Path.Combine(instanceDirectory, StateFileName);

    private static string NormalizeCacheKey(string relativePath)
        => relativePath.Replace('\\', '/').TrimStart('/');

    private static Dictionary<string, HashCacheEntry> LoadHashCache(string instanceDirectory)
    {
        try
        {
            string path = GetStateFilePath(instanceDirectory);
            if (!File.Exists(path)) return new Dictionary<string, HashCacheEntry>(StringComparer.Ordinal);

            var raw = JsonSerializer.Deserialize<Dictionary<string, HashCacheEntry>>(File.ReadAllText(path));
            return raw ?? new Dictionary<string, HashCacheEntry>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, HashCacheEntry>(StringComparer.Ordinal);
        }
    }

    private static void SaveHashCache(string instanceDirectory, Dictionary<string, HashCacheEntry> cache)
    {
        try
        {
            string path = GetStateFilePath(instanceDirectory);
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Cache é otimização — falha de escrita não deve abortar a sincronização
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
