using Prometheus.Services.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LcuCompanionSettingsTests
    {
        [Fact]
        public void Settings_DefaultToTrueAndPersistChanges()
        {
            var directory = CreateTemporaryDirectory();
            var settingsPath = Path.Combine(directory, "lcu-companion.json");

            try
            {
                var settings = new LcuCompanionSettings(settingsPath);

                Assert.True(settings.IsEnabled);
                Assert.True(settings.AutoShowMatchOnGameStart);

                settings.IsEnabled = false;
                settings.AutoShowMatchOnGameStart = false;
                var reloaded = new LcuCompanionSettings(settingsPath);

                Assert.False(reloaded.IsEnabled);
                Assert.False(reloaded.AutoShowMatchOnGameStart);
                Assert.True(settings.LastPersistenceSucceeded);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void CorruptFile_FallsBackToEnabled()
        {
            var directory = CreateTemporaryDirectory();
            var settingsPath = Path.Combine(directory, "lcu-companion.json");

            try
            {
                File.WriteAllText(settingsPath, "not-json");

                var settings = new LcuCompanionSettings(settingsPath);

                Assert.True(settings.IsEnabled);
                Assert.True(settings.AutoShowMatchOnGameStart);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LegacyFile_WithoutAutoShowSetting_DefaultsAutoShowToTrue()
        {
            var directory = CreateTemporaryDirectory();
            var settingsPath = Path.Combine(directory, "lcu-companion.json");

            try
            {
                File.WriteAllText(settingsPath, "{\"IsEnabled\":false}");

                var settings = new LcuCompanionSettings(settingsPath);

                Assert.False(settings.IsEnabled);
                Assert.True(settings.AutoShowMatchOnGameStart);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Prometheus.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
