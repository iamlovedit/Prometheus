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
                    AutoSwapAramBench = true,
                    PreferredPickChampionIds = [103, 22, 103, 0],
                    PreferredBanChampionIds = [84, 55, 84, -1],
                    AutoPickChampion = true,
                    AutoBanChampion = true
                };

                var reloaded = new GameAutomationSettings(settingsPath);

                Assert.True(reloaded.AutoSwapAramBench);
                Assert.Equal([22, 103], reloaded.PreferredAramChampionIds);
                Assert.True(reloaded.AutoPickChampion);
                Assert.True(reloaded.AutoBanChampion);
                Assert.Equal([103, 22], reloaded.PreferredPickChampionIds);
                Assert.Equal([84, 55], reloaded.PreferredBanChampionIds);
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
                Assert.False(settings.AutoPickChampion);
                Assert.False(settings.AutoBanChampion);
                Assert.Empty(settings.PreferredPickChampionIds);
                Assert.Empty(settings.PreferredBanChampionIds);
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
