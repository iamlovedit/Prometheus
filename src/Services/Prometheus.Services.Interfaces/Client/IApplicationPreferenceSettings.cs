namespace Prometheus.Services.Interfaces.Client
{
    public interface IApplicationPreferenceSettings
    {
        int? LanguageIndex { get; }

        int? ThemeIndex { get; }

        bool? LoggingEnabled { get; }

        bool SaveLanguageIndex(int languageIndex);

        bool SaveThemeIndex(int themeIndex);

        bool SaveLoggingEnabled(bool enabled);
    }
}
