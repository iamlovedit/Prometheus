using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class QuickMatchSettingsTests
    {
        [Fact]
        public void MissingSettings_DefaultsToRankedSoloDuo()
        {
            using var directory = new TemporaryDirectory();
            var settings = new QuickMatchSettings(
                Path.Combine(directory.Path, "quick-match.json"));

            Assert.Equal(GameQueueIds.RankedSoloDuo, settings.QueueId);
        }

        [Fact]
        public void SaveQueueId_PersistsSelectionAcrossInstances()
        {
            using var directory = new TemporaryDirectory();
            var path = Path.Combine(directory.Path, "quick-match.json");
            var settings = new QuickMatchSettings(path);

            Assert.True(settings.SaveQueueId(GameQueueIds.HextechAram));

            var reloaded = new QuickMatchSettings(path);
            Assert.Equal(GameQueueIds.HextechAram, reloaded.QueueId);
        }

        [Fact]
        public void CorruptedSettings_DefaultsToRankedSoloDuo()
        {
            using var directory = new TemporaryDirectory();
            var path = Path.Combine(directory.Path, "quick-match.json");
            File.WriteAllText(path, "{not-json");

            var settings = new QuickMatchSettings(path);

            Assert.Equal(GameQueueIds.RankedSoloDuo, settings.QueueId);
        }

        [Fact]
        public void UnsupportedPersistedQueue_DefaultsToRankedSoloDuo()
        {
            using var directory = new TemporaryDirectory();
            var path = Path.Combine(directory.Path, "quick-match.json");
            File.WriteAllText(path, "{\"QueueId\":9999}");

            var settings = new QuickMatchSettings(path);

            Assert.Equal(GameQueueIds.RankedSoloDuo, settings.QueueId);
        }

        [Fact]
        public void UnsupportedQueueId_ThrowsWithoutChangingSelection()
        {
            using var directory = new TemporaryDirectory();
            var settings = new QuickMatchSettings(
                Path.Combine(directory.Path, "quick-match.json"));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                settings.SaveQueueId(9999));
            Assert.Equal(GameQueueIds.RankedSoloDuo, settings.QueueId);
        }

        [Fact]
        public void SaveQueueId_WhenSelectionChanges_RaisesChanged()
        {
            using var directory = new TemporaryDirectory();
            var settings = new QuickMatchSettings(
                Path.Combine(directory.Path, "quick-match.json"));
            var changedCount = 0;
            settings.Changed += (_, _) => changedCount++;

            settings.SaveQueueId(GameQueueIds.RankedFlex);
            settings.SaveQueueId(GameQueueIds.RankedFlex);

            Assert.Equal(GameQueueIds.RankedFlex, settings.QueueId);
            Assert.Equal(1, changedCount);
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"prometheus-quick-match-{Guid.NewGuid():N}");
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
        }
    }
}
