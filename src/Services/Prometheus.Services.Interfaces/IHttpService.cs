using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces
{
    public interface IHttpService
    {
        void Initialize(int port, string token);

        Task<string> GetAsync(string url, IEnumerable<string> queryParameters = null);

        Task<string> PostAsync(string url, object body, IEnumerable<string> queryParameters);

        Task<byte[]> GetByteArrayResponseAsync(HttpMethod httpMethod, string url, IEnumerable<string> queryParameters = null);

        Task<string> SendAsync(HttpMethod httpMethod, string url, object body, IEnumerable<string> queryParameters);
    }
}
