using Prometheus.Services.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class ApplicationPreferenceSettingsTests
    {
        [Fact]
        public void DefaultPath_UsesStableLocalApplicationDataDirectory()
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Prometheus",
                "application-preferences.json");

            Assert.Equal(expected, ApplicationPreferenceSettings.DefaultSettingsPath);
        }

        [Fact]
        public void Preferences_ArePersistedAndReloadedFromStableFile()
        {
            var fixture = CreateFixture();

            try
            {
                var settings = new ApplicationPreferenceSettings(fixture.SettingsPath);

                Assert.True(settings.SaveLanguageIndex(1));
                Assert.True(settings.SaveThemeIndex(2));
                Assert.True(settings.SaveLoggingEnabled(true));

                var reloaded = new ApplicationPreferenceSettings(fixture.SettingsPath);

                Assert.Equal(1, reloaded.LanguageIndex);
                Assert.Equal(2, reloaded.ThemeIndex);
                Assert.True(reloaded.LoggingEnabled);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public void SavingMissingPreference_PreservesExistingValues()
        {
            var fixture = CreateFixture();

            try
            {
                Directory.CreateDirectory(fixture.DirectoryPath);
                File.WriteAllText(
                    fixture.SettingsPath,
                    "{\"LanguageIndex\":1,\"LoggingEnabled\":false}");
                var settings = new ApplicationPreferenceSettings(fixture.SettingsPath);

                Assert.True(settings.SaveThemeIndex(2));

                var reloaded = new ApplicationPreferenceSettings(fixture.SettingsPath);
                Assert.Equal(1, reloaded.LanguageIndex);
                Assert.Equal(2, reloaded.ThemeIndex);
                Assert.False(reloaded.LoggingEnabled);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Fact]
        public void UnsupportedOrCorruptValues_AreTreatedAsMissing()
        {
            var fixture = CreateFixture();

            try
            {
                Directory.CreateDirectory(fixture.DirectoryPath);
                File.WriteAllText(
                    fixture.SettingsPath,
                    "{\"LanguageIndex\":4,\"ThemeIndex\":8,\"LoggingEnabled\":true}");

                var unsupported = new ApplicationPreferenceSettings(fixture.SettingsPath);

                Assert.Null(unsupported.LanguageIndex);
                Assert.Null(unsupported.ThemeIndex);
                Assert.True(unsupported.LoggingEnabled);

                File.WriteAllText(fixture.SettingsPath, "not-json");
                var corrupt = new ApplicationPreferenceSettings(fixture.SettingsPath);

                Assert.Null(corrupt.LanguageIndex);
                Assert.Null(corrupt.ThemeIndex);
                Assert.Null(corrupt.LoggingEnabled);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static SettingsFixture CreateFixture()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Prometheus.ApplicationPreferenceSettingsTests",
                Guid.NewGuid().ToString("N"));
            return new SettingsFixture(
                directory,
                Path.Combine(directory, "application-preferences.json"));
        }

        private sealed record SettingsFixture(
            string DirectoryPath,
            string SettingsPath) : IDisposable
        {
            public void Dispose()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
        }
    }
}
