using Prometheus.Services.Interfaces;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Prometheus.Services
{
    public class HttpService : HttpServiceBase, IHttpService
    {
        public async Task<string> GetAsync(string url, IEnumerable<string> queryParameters)
        {
            if (!_isInitialized)
            {
                return default;
            }
            var responseMessage = await GetHttpMessageAsync(url, queryParameters);
            return await responseMessage.Content.ReadAsStringAsync();
        }

        public async Task<T> GetAsync<T>(string url, IEnumerable<string> queryParameters = null) where T : class, new()
        {
            if (!_isInitialized)
            {
                return default;
            }
            var responseMessage = await GetHttpMessageAsync(url, queryParameters);
            return await responseMessage.Content.ReadFromJsonAsync<T>();
        }

        public async Task<byte[]> GetByteArrayResponseAsync(HttpMethod httpMethod, string url, IEnumerable<string> queryParameters = null)
        {
            if (!_isInitialized)
            {
                return default;
            }
            var relativeUrl = BuildRelativeUrl(url, queryParameters);
            var requestMessage = new HttpRequestMessage(httpMethod, relativeUrl);
            var responseMessage = await _httpClient.SendAsync(requestMessage);
            responseMessage.EnsureSuccessStatusCode();
            return await responseMessage.Content.ReadAsByteArrayAsync();
        }

        public async Task<string> PostAsync(string url, object body, IEnumerable<string> queryParameters)
        {
            if (!_isInitialized)
            {
                return default;
            }
            var responseMessage = await PostHttpMessageAsync(url, body, queryParameters);
            return await responseMessage.Content.ReadAsStringAsync();
        }

        public async Task<T> PostAsync<T>(string url, object body, IEnumerable<string> queryParameters = null) where T : class, new()
        {
            if (!_isInitialized)
            {
                return default;
            }
            var responseMessage = await PostHttpMessageAsync(url, body, queryParameters);
            return await responseMessage.Content.ReadFromJsonAsync<T>();
        }

        public async Task PostAsync(string url, object body)
        {
            if (!_isInitialized)
            {
                return;
            }
            var response = await _httpClient.PostAsJsonAsync(url, body);
            response.EnsureSuccessStatusCode();
        }

        public async Task<string> SendAsync(HttpMethod httpMethod, string url, object body, IEnumerable<string> queryParameters)
        {
            if (!_isInitialized)
            {
                return default;
            }
            var responseMessage = await SendHttpMessageAsync(httpMethod, url, body, queryParameters);
            return await responseMessage.Content.ReadAsStringAsync();
        }

        public async Task<T> SendAsync<T>(HttpMethod httpMethod, string url, object body, IEnumerable<string> queryParameters = null) where T : class, new()
        {
            if (!_isInitialized)
            {
                return default;
            }
            var responseMessage = await SendHttpMessageAsync(httpMethod, url, body, queryParameters);
            return await responseMessage.Content.ReadFromJsonAsync<T>();
        }
    }
}
