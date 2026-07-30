
namespace Prometheus.Services.Interfaces
{
    public interface IHttpService
    {
        void Initialize(int port, string token);

        /// <summary>
        /// Clears the active LCU connection. Subsequent requests return their default value
        /// until the service is initialized again.
        /// </summary>
        void Reset();

        bool IsInitialized { get; }

        Task<T> GetAsync<T>(string url, IEnumerable<string> queryParameters = null,
            CancellationToken cancellationToken = default) where T : class, new();

        Task<string> GetAsync(string url, IEnumerable<string> queryParameters = null,
            CancellationToken cancellationToken = default);

        Task<T> PostAsync<T>(string url, object body, IEnumerable<string> queryParameters = null,
            CancellationToken cancellationToken = default) where T : class, new();

        Task PostAsync(string url, object body);

        Task PostAsync(string url, object body, CancellationToken cancellationToken);

        Task<string> PostAsync(string url, object body, IEnumerable<string> queryParameters = null,
            CancellationToken cancellationToken = default);

        Task<byte[]> GetByteArrayResponseAsync(HttpMethod httpMethod, string url,
            IEnumerable<string> queryParameters = null, CancellationToken cancellationToken = default);

        Task<T> SendAsync<T>(HttpMethod httpMethod, string url, object body,
            IEnumerable<string> queryParameters = null,
            CancellationToken cancellationToken = default) where T : class, new();

        Task<string> SendAsync(HttpMethod httpMethod, string url, object body,
            IEnumerable<string> queryParameters = null,
            CancellationToken cancellationToken = default);
    }
}
