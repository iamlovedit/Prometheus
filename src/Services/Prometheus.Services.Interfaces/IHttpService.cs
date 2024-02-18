using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces
{
    public interface IHttpService
    {
        void Initialize(int port, string token);

        Task<string> GetAsync();
    }
}
