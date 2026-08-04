using Prometheus.Core.Models;
using System.Globalization;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class ApplicationPreferenceDefaultsTests
    {
        [Theory]
        [InlineData("zh-CN", 0)]
        [InlineData("zh-TW", 0)]
        [InlineData("en-US", 1)]
        [InlineData("ja-JP", 1)]
        public void ResolveLanguageIndex_MapsSupportedApplicationLanguage(
            string cultureName,
            int expected)
        {
            var result = ApplicationPreferenceDefaults.ResolveLanguageIndex(
                CultureInfo.GetCultureInfo(cultureName));

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true, ApplicationThemeMode.Light)]
        [InlineData(false, ApplicationThemeMode.Dark)]
        public void ResolveThemeMode_MapsWindowsApplicationTheme(
            bool systemUsesLightTheme,
            ApplicationThemeMode expected)
        {
            var result = ApplicationPreferenceDefaults.ResolveThemeMode(
                systemUsesLightTheme);

            Assert.Equal(expected, result);
        }
    }
}
