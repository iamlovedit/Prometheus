using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces
{
    public interface IHttpService
    {
        void Initialize(int port, string token);

        Task<T> GetAsync<T>(string url, IEnumerable<string> queryParameters = null) where T : class, new();

        Task<string> GetAsync(string url, IEnumerable<string> queryParameters = null);

        Task<T> PostAsync<T>(string url, object body, IEnumerable<string> queryParameters = null) where T : class, new();

        Task PostAsync(string url, object body);

        Task<string> PostAsync(string url, object body, IEnumerable<string> queryParameters = null);

        Task<byte[]> GetByteArrayResponseAsync(HttpMethod httpMethod, string url, IEnumerable<string> queryParameters = null);

        Task<T> SendAsync<T>(HttpMethod httpMethod, string url, object body, IEnumerable<string> queryParameters = null) where T : class, new();

        Task<string> SendAsync(HttpMethod httpMethod, string url, object body, IEnumerable<string> queryParameters = null);
    }
}
