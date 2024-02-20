using System;

namespace Prometheus.Services.Interfaces
{
    public interface IResourceService
    {
        T FindResource<T>(string resourceKey);

        string GetLanguageResourceUri(string language);

        void AddResourceDictionary(Uri resourceUri);

        void RemoveResourceDictionary(Uri resourceUri);
    }
}
