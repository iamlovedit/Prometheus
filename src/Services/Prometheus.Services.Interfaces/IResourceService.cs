namespace Prometheus.Services.Interfaces
{
    public interface IResourceService
    {
        T FindResource<T>(string resourceKey);
    }
}
