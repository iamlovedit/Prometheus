using Prometheus.Core.Models;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class ApplicationThemeModeResolverTests
    {
        [Theory]
        [InlineData(ApplicationThemeMode.Light, true, false)]
        [InlineData(ApplicationThemeMode.Light, false, false)]
        [InlineData(ApplicationThemeMode.Dark, true, true)]
        [InlineData(ApplicationThemeMode.Dark, false, true)]
        [InlineData(ApplicationThemeMode.System, true, false)]
        [InlineData(ApplicationThemeMode.System, false, true)]
        public void ShouldUseDarkTheme_ResolvesSelectedMode(
            ApplicationThemeMode mode,
            bool systemUsesLightTheme,
            bool expected)
        {
            var result = ApplicationThemeModeResolver.ShouldUseDarkTheme(
                mode,
                systemUsesLightTheme);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(-1, ApplicationThemeMode.Light)]
        [InlineData(0, ApplicationThemeMode.Light)]
        [InlineData(1, ApplicationThemeMode.Dark)]
        [InlineData(2, ApplicationThemeMode.System)]
        [InlineData(3, ApplicationThemeMode.Light)]
        public void Normalize_ReturnsSupportedMode(
            int value,
            ApplicationThemeMode expected)
        {
            Assert.Equal(expected, ApplicationThemeModeResolver.Normalize(value));
        }
    }
}
