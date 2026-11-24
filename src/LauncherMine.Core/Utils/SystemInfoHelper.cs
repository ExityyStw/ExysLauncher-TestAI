using System.Runtime.InteropServices;

namespace LauncherMine.Core.Utils;

/// <summary>
/// Utilitário nativo para detecção precisa da memória física instalada no computador.
/// </summary>
public static class SystemInfoHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Retorna a memória RAM física total instalada no computador em Megabytes (MB).
    /// Exemplo: 16 GB = 16384 MB.
    /// </summary>
    public static int GetTotalPhysicalRamMb()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    long totalMb = (long)(memStatus.ullTotalPhys / (1024 * 1024));
                    return (int)Math.Clamp(totalMb, 2048, 131072);
                }
            }
            else if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out long kb))
                        {
                            return (int)(kb / 1024);
                        }
                    }
                }
            }

            var gcInfo = GC.GetGCMemoryInfo();
            if (gcInfo.TotalAvailableMemoryBytes > 0)
            {
                return (int)(gcInfo.TotalAvailableMemoryBytes / (1024 * 1024));
            }
        }
        catch
        {
            // Fallback seguro caso falhe a chamada nativa
        }

        return 8192; // 8 GB padrão seguro
    }

    /// <summary>
    /// Calcula a quantidade máxima de RAM recomendada para o jogo,
    /// reservando no mínimo 2 GB para o sistema operacional Windows/Linux.
    /// </summary>
    public static int GetSafeMaxRamMb(int totalRamMb)
    {
        // Se tem 16 GB (16384 MB), reserva 2.5 GB para o SO e navegador = 13824 MB máximo seguro
        if (totalRamMb >= 16384)
            return totalRamMb - 2560;

        // Se tem 8 GB (8192 MB), reserva 2 GB = 6144 MB máximo seguro
        if (totalRamMb >= 8192)
            return totalRamMb - 2048;

        return Math.Max(2048, totalRamMb - 1024);
    }
}
