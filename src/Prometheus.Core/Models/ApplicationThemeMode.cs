namespace Prometheus.Core.Models
{
    public enum ApplicationThemeMode
    {
        Light = 0,
        Dark = 1,
        System = 2
    }

    public static class ApplicationThemeModeResolver
    {
        public static ApplicationThemeMode Normalize(int value)
        {
            return value switch
            {
                (int)ApplicationThemeMode.Dark => ApplicationThemeMode.Dark,
                (int)ApplicationThemeMode.System => ApplicationThemeMode.System,
                _ => ApplicationThemeMode.Light
            };
        }

        public static bool ShouldUseDarkTheme(
            ApplicationThemeMode mode,
            bool systemUsesLightTheme)
        {
            return mode == ApplicationThemeMode.Dark
                || mode == ApplicationThemeMode.System && !systemUsesLightTheme;
        }
    }
}
