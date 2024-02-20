using Prometheus.Services.Interfaces;
using System;
using System.Windows;

namespace Prometheus.Services
{
    public class ResourceService : IResourceService
    {
        private readonly string _resourceUriFormat = "pack://application:,,,/Prometheus.Core;component/Resources/Languages/{0}.xaml";

        public void AddResourceDictionary(Uri resourceUri)
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = resourceUri });
        }
        public void RemoveResourceDictionary(Uri resourceUri)
        {
            Application.Current.Resources.MergedDictionaries.Remove(new ResourceDictionary() { Source = resourceUri });
        }

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
