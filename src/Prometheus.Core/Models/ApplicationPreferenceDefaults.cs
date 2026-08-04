using System.Globalization;

namespace Prometheus.Core.Models
{
    public static class ApplicationPreferenceDefaults
    {
        public static int ResolveLanguageIndex(CultureInfo uiCulture)
        {
            ArgumentNullException.ThrowIfNull(uiCulture);

            return string.Equals(
                uiCulture.TwoLetterISOLanguageName,
                "zh",
                StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
        }

        public static ApplicationThemeMode ResolveThemeMode(bool systemUsesLightTheme)
        {
            return systemUsesLightTheme
                ? ApplicationThemeMode.Light
                : ApplicationThemeMode.Dark;
        }
    }
}
