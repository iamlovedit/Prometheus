using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Prometheus.Services
{
    public abstract class HttpServiceBase
    {
        protected HttpClient _httpClient;

        protected bool _isInitialized;

        protected readonly string _jsonType = "application/json";

        protected virtual string BuildQueryStringFromParameters(IEnumerable<string> queryParameters)
        {
            return "?" + string.Join("&", queryParameters.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        protected virtual string BuildRelativeUrl(string url, IEnumerable<string> queryParameters)
        {
            return queryParameters == null ? url : url + BuildQueryStringFromParameters(queryParameters);
        }

        public virtual void Initialize(int port, string token)
        {
            var httpClientHandler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback = (response, cert, chain, errors) => true
            };

            var tokenBytes = Encoding.ASCII.GetBytes($"riot:{token}");
            _httpClient = new HttpClient(httpClientHandler)
            {
                BaseAddress = new Uri($"https://127.0.0.1:{port}/"),
                DefaultRequestVersion = new Version(2, 0),
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(_jsonType));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "LeagueOfLegendsClient/12.7.433.4138 (CEF 91)");
            _httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(tokenBytes));
            _isInitialized = true;
        }

        protected virtual async Task<HttpResponseMessage> GetHttpMessageAsync(string url, IEnumerable<string> queryParameters)
        {
            if (!_isInitialized)
            {
                return default;
            }
            var relativeUrl = BuildRelativeUrl(url, queryParameters);
            var responseMessage = await _httpClient.GetAsync(relativeUrl);
            responseMessage.EnsureSuccessStatusCode();
            return responseMessage;
        }

        protected virtual async Task<HttpResponseMessage> PostHttpMessageAsync(string url, object body, IEnumerable<string> queryParameters)
        {
            if (!_isInitialized)
            {
                return default;
            }
            var relativeUrl = BuildRelativeUrl(url, queryParameters);
            var responseMessage = await _httpClient.PostAsJsonAsync(relativeUrl, body);
            responseMessage.EnsureSuccessStatusCode();
            return responseMessage;
        }

        protected virtual async Task<HttpResponseMessage> SendHttpMessageAsync(HttpMethod httpMethod, string url, object body, IEnumerable<string> queryParameters)
        {
            if (!_isInitialized)
            {
                return default;
            }
            var relativeUrl = BuildRelativeUrl(url, queryParameters);
            var requestMessage = new HttpRequestMessage(httpMethod, relativeUrl)
            {
                Content = new StringContent(body?.ToString(), Encoding.UTF8, _jsonType)
            };
            var responseMessage = await _httpClient.SendAsync(requestMessage);
            responseMessage.EnsureSuccessStatusCode();
            return responseMessage;
        }
    }
}
