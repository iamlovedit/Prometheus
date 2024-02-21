using System;

namespace Prometheus.Services.Interfaces
{
    public interface IResourceService
    {
        T FindResource<T>(string resourceKey);

        string GetLanguageResourceUri(string language);

        void SwitchTheme(int themeIndex);

        void SwitchLanguage(int languageIndex);
    }
}
