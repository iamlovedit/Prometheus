using Prometheus.Services.Interfaces;
using System.Windows;

namespace Prometheus.Services
{
    public class ResourceService : IResourceService
    {
        public T FindResource<T>(string resourceKey)
        {
            return (T)Application.Current.FindResource(resourceKey);
        }
    }
}
