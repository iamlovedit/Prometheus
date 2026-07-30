using Prometheus.Core.Models;
using Prometheus.Services.Client;
using System;
using System.IO;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class ProfilePresentationSettingsTests
    {
        [Fact]
        public void SavedValues_AreLoadedByNextInstance()
        {
            var directory = CreateTemporaryDirectory();
            var settingsPath = Path.Combine(directory, "profile-presentation.json");

            try
            {
                var settings = new ProfilePresentationSettings(settingsPath);
                settings.SaveOnlineStatus("away");
                settings.SaveStatusMessage("Ready to play");
                settings.SaveTier(
                    QueueType.RANKED_FLEX_SR,
                    Tier.EMERALD,
                    Division.II);

                var reloaded = new ProfilePresentationSettings(settingsPath);

                Assert.Equal("away", reloaded.OnlineStatus);
                Assert.Equal("Ready to play", reloaded.StatusMessage);
                Assert.Equal(QueueType.RANKED_FLEX_SR, reloaded.QueueType);
                Assert.Equal(Tier.EMERALD, reloaded.Tier);
                Assert.Equal(Division.II, reloaded.Division);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void EmptyStatusMessage_RemainsConfiguredAfterReload()
        {
            var directory = CreateTemporaryDirectory();
            var settingsPath = Path.Combine(directory, "profile-presentation.json");

            try
            {
                var settings = new ProfilePresentationSettings(settingsPath);
                settings.SaveStatusMessage(string.Empty);

                var reloaded = new ProfilePresentationSettings(settingsPath);

                Assert.Equal(string.Empty, reloaded.StatusMessage);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void CorruptFile_LoadsAsUnconfigured()
        {
            var directory = CreateTemporaryDirectory();
            var settingsPath = Path.Combine(directory, "profile-presentation.json");

            try
            {
                File.WriteAllText(settingsPath, "not-json");

                var settings = new ProfilePresentationSettings(settingsPath);

                Assert.Null(settings.OnlineStatus);
                Assert.Null(settings.StatusMessage);
                Assert.Null(settings.QueueType);
                Assert.Null(settings.Tier);
                Assert.Null(settings.Division);
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
