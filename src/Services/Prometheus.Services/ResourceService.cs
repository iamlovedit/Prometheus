using Prometheus.Services.Interfaces;
using System.Windows;

namespace Prometheus.Services
{
    public class ResourceService : IResourceService
    {
        private readonly string _resourceUriFormat = "pack://application:,,,/Prometheus.Core;component/Resources/Languages/{0}.xaml";

        public T FindResource<T>(string resourceKey)
        {
            return (T)Application.Current.FindResource(resourceKey);
        }

        public string GetLanguageResourceUri(string language)
        {
            return string.Format(_resourceUriFormat, language);
        }
    }
}
