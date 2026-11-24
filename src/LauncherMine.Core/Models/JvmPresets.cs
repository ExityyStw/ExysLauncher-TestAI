namespace LauncherMine.Core.Models;

/// <summary>
/// Predefinições profissionais de Garbage Collector e JVM flags
/// otimizadas para modpacks pesados de 300+ mods no Minecraft 1.20+ e 1.21+ (Java 21).
/// </summary>
public static class JvmPresets
{
    /// <summary>
    /// Presets pré-configurados testados para eliminar travamentos e stuttering.
    /// </summary>
    public static readonly Dictionary<string, List<string>> Presets = new()
    {
        ["Generational ZGC (Java 21+ • Ultra Baixa Latência)"] = [
            "-XX:+UseZGC",
            "-XX:+ZGenerational",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+AlwaysPreTouch",
            "-XX:+DisableExplicitGC"
        ],

        ["G1GC Ultra-Otimizado (Modpacks 300+ Mods • Recomendado)"] = [
            "-XX:+UseG1GC",
            "-XX:+ParallelRefProcEnabled",
            "-XX:MaxGCPauseMillis=150",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+DisableExplicitGC",
            "-XX:+AlwaysPreTouch",
            "-XX:G1NewSizePercent=30",
            "-XX:G1MaxNewSizePercent=40",
            "-XX:G1ReservePercent=20",
            "-XX:G1HeapWastePercent=5",
            "-XX:G1MixedGCCountTarget=4",
            "-XX:InitiatingHeapOccupancyPercent=15",
            "-XX:G1MixedGCLiveThresholdPercent=90",
            "-XX:G1RSetUpdatingPauseTimePercent=5",
            "-XX:SurvivorRatio=32"
        ],

        ["Shenandoah GC (Pausas Ultracurtas)"] = [
            "-XX:+UseShenandoahGC",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:ShenandoahGCHeuristics=adaptive",
            "-XX:+AlwaysPreTouch",
            "-XX:+DisableExplicitGC"
        ],

        ["Padrão Vanilla"] = [
            "-XX:+UseG1GC",
            "-XX:MaxGCPauseMillis=200"
        ]
    };
}
