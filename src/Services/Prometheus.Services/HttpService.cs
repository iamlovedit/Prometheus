using Newtonsoft.Json;
using Prometheus.Services.Interfaces;

namespace Prometheus.Services
{
    public class HttpService : HttpServiceBase, IHttpService
    {
        public async Task<string> GetAsync(string url, IEnumerable<string> queryParameters = null,
            CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                return default;
            }

            using var responseMessage = await GetHttpMessageAsync(
                url, queryParameters, cancellationToken).ConfigureAwait(false);
            if (responseMessage is null)
            {
                return default;
            }

            return await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> GetAsync<T>(string url, IEnumerable<string> queryParameters = null,
            CancellationToken cancellationToken = default) where T : class, new()
        {
            var json = await GetAsync(url, queryParameters, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
        }

        public async Task<byte[]> GetByteArrayResponseAsync(HttpMethod httpMethod, string url,
            IEnumerable<string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                return default;
            }

            if (!TryCreateRequestMessage(httpMethod, url, queryParameters, null, false,
                    out var client, out var requestMessage))
            {
                return default;
            }

            using var request = requestMessage;
            using var responseMessage = await client.SendAsync(requestMessage,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            responseMessage.EnsureSuccessStatusCode();
            return await responseMessage.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> PostAsync(string url, object body,
            IEnumerable<string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                return default;
            }

            using var responseMessage = await PostHttpMessageAsync(
                url, body, queryParameters, cancellationToken).ConfigureAwait(false);
            if (responseMessage is null)
            {
                return default;
            }

            return await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> PostAsync<T>(string url, object body,
            IEnumerable<string> queryParameters = null, CancellationToken cancellationToken = default)
            where T : class, new()
        {
            var json = await PostAsync(url, body, queryParameters, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
        }

        public Task PostAsync(string url, object body)
        {
            return PostAsync(url, body, CancellationToken.None);
        }

        public async Task PostAsync(string url, object body, CancellationToken cancellationToken)
        {
            if (!_isInitialized)
            {
                return;
            }

            using var responseMessage = await PostHttpMessageAsync(
                url, body, null, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> SendAsync(HttpMethod httpMethod, string url, object body,
            IEnumerable<string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                return default;
            }

            using var responseMessage = await SendHttpMessageAsync(
                httpMethod, url, body, queryParameters, cancellationToken).ConfigureAwait(false);
            if (responseMessage is null)
            {
                return default;
            }

            return await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> SendAsync<T>(HttpMethod httpMethod, string url, object body,
            IEnumerable<string> queryParameters = null, CancellationToken cancellationToken = default)
            where T : class, new()
        {
            var json = await SendAsync(
                httpMethod, url, body, queryParameters, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
        }
    }
}
