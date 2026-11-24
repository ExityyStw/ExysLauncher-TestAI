namespace LauncherMine.Core.Updater;

public sealed record UpdateProgress(
    int TotalFiles, 
    int ProcessedFiles, 
    long TotalBytes, 
    long DownloadedBytes, 
    string CurrentFileName
)
{
    public double ProgressPercentage => TotalBytes > 0 
        ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0.0, 100.0) 
        : (TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100.0 : 0.0);
}
