using Prometheus.Services.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class GameAutomationSettingsTests
    {
        [Fact]
        public void AramPreferences_AreNormalizedAndPersisted()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Prometheus.Tests",
                Guid.NewGuid().ToString("N"));
            var settingsPath = Path.Combine(directory, "game-automation.json");

            try
            {
                var settings = new GameAutomationSettings(settingsPath)
                {
                    PreferredAramChampionIds = [22, 103, 22, 0, -1],
                    AutoSwapAramBench = true
                };

                var reloaded = new GameAutomationSettings(settingsPath);

                Assert.True(reloaded.AutoSwapAramBench);
                Assert.Equal([22, 103], reloaded.PreferredAramChampionIds);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Fact]
        public void CorruptFile_DisablesAramAutomationAndClearsPreferences()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Prometheus.Tests",
                Guid.NewGuid().ToString("N"));
            var settingsPath = Path.Combine(directory, "game-automation.json");

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(settingsPath, "not-json");

                var settings = new GameAutomationSettings(settingsPath);

                Assert.False(settings.AutoSwapAramBench);
                Assert.Empty(settings.PreferredAramChampionIds);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
