using Prometheus.Update;

namespace Prometheus.Updater;

internal static class BootstrapperLog
{
    private static readonly object SyncRoot = new();
    private static readonly string LogPath = Path.Combine(UpdatePaths.GetLocalDataRoot(),
        "Logs", "updater.log");

    public static void Write(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never prevent launch or rollback.
        }
    }
}
