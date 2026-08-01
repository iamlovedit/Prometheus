using Prometheus.Services;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LogFileRetentionCleanerTests
    {
        [Fact]
        public void DeleteExpiredFiles_RemovesOnlyMatchingLogsOlderThanRetentionPeriod()
        {
            var testDirectory = Path.Combine(
                Path.GetTempPath(),
                "Prometheus.LogFileRetentionCleanerTests",
                Guid.NewGuid().ToString("N"));
            var nestedDirectory = Path.Combine(testDirectory, "Nested");
            var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

            try
            {
                Directory.CreateDirectory(nestedDirectory);
                var expiredLog = CreateFile(
                    testDirectory,
                    "prometheus-20260723.jsonl",
                    now.AddDays(-8));
                var boundaryLog = CreateFile(
                    testDirectory,
                    "prometheus-20260725.jsonl",
                    now.AddDays(-7));
                var recentLog = CreateFile(
                    testDirectory,
                    "prometheus-20260731.jsonl",
                    now.AddDays(-1));
                var unrelatedFile = CreateFile(
                    testDirectory,
                    "other-application.jsonl",
                    now.AddDays(-30));
                var nestedLog = CreateFile(
                    nestedDirectory,
                    "prometheus-20260701.jsonl",
                    now.AddDays(-30));

                var result = LogFileRetentionCleaner.DeleteExpiredFiles(
                    testDirectory,
                    "prometheus-*.jsonl",
                    TimeSpan.FromDays(7),
                    now);

                Assert.Equal(1, result.DeletedCount);
                Assert.Equal(0, result.FailureCount);
                Assert.False(File.Exists(expiredLog));
                Assert.True(File.Exists(boundaryLog));
                Assert.True(File.Exists(recentLog));
                Assert.True(File.Exists(unrelatedFile));
                Assert.True(File.Exists(nestedLog));
            }
            finally
            {
                if (Directory.Exists(testDirectory))
                {
                    Directory.Delete(testDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void DeleteExpiredFiles_WhenDirectoryDoesNotExist_DoesNotCreateIt()
        {
            var testDirectory = Path.Combine(
                Path.GetTempPath(),
                "Prometheus.LogFileRetentionCleanerTests",
                Guid.NewGuid().ToString("N"));

            var result = LogFileRetentionCleaner.DeleteExpiredFiles(
                testDirectory,
                "prometheus-*.jsonl",
                TimeSpan.FromDays(7),
                DateTimeOffset.UtcNow);

            Assert.Equal(default, result);
            Assert.False(Directory.Exists(testDirectory));
        }

        private static string CreateFile(
            string directory,
            string fileName,
            DateTimeOffset lastWriteTime)
        {
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "test");
            File.SetLastWriteTimeUtc(path, lastWriteTime.UtcDateTime);
            return path;
        }
    }
}
