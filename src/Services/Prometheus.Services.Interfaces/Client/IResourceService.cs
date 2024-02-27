using System;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IResourceService
    {
        T FindResource<T>(string resourceKey);

        string GetLanguageResourceUri(string language);

        void SwitchTheme(int themeIndex);

        void SwitchLanguage(int languageIndex);

        string GetTierIconResourceUri(string tier);
    }
}
