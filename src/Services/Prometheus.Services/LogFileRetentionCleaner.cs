namespace Prometheus.Services
{
    public readonly record struct LogFileCleanupResult(
        int DeletedCount,
        int FailureCount);

    /// <summary>
    /// Deletes expired application log files from one explicitly-scoped directory. Cleanup is
    /// best effort so an inaccessible file cannot prevent the application from starting.
    /// </summary>
    public static class LogFileRetentionCleaner
    {
        public static LogFileCleanupResult DeleteExpiredFiles(
            string directory,
            string searchPattern,
            TimeSpan retentionPeriod,
            DateTimeOffset utcNow)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A log directory is required.", nameof(directory));
            }

            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                throw new ArgumentException("A log file search pattern is required.",
                    nameof(searchPattern));
            }

            if (retentionPeriod <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retentionPeriod),
                    retentionPeriod,
                    "The retention period must be positive.");
            }

            if (!Directory.Exists(directory))
            {
                return default;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(
                    directory,
                    searchPattern,
                    SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return new LogFileCleanupResult(0, 1);
            }
            catch (UnauthorizedAccessException)
            {
                return new LogFileCleanupResult(0, 1);
            }

            var cutoffUtc = utcNow.ToUniversalTime().UtcDateTime - retentionPeriod;
            var deletedCount = 0;
            var failureCount = 0;
            foreach (var file in files)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoffUtc)
                    {
                        continue;
                    }

                    File.Delete(file);
                    deletedCount++;
                }
                catch (IOException)
                {
                    failureCount++;
                }
                catch (UnauthorizedAccessException)
                {
                    failureCount++;
                }
            }

            return new LogFileCleanupResult(deletedCount, failureCount);
        }
    }
}
