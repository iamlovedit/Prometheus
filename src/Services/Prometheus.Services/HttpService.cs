using Prometheus.Services.Interfaces;
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
    public class HttpService : IHttpService
    {
        private readonly string _jsonType = "application/json";

        private HttpClient _httpClient;

        public async Task<string> GetAsync(string url, IEnumerable<string> queryParameters)
        {
            var targetUrl = queryParameters == null ? url : BuildQueryString(queryParameters);
            return await _httpClient.GetStringAsync(targetUrl);
        }

        public async Task<byte[]> GetByteArrayResponseAsync(HttpMethod httpMethod, string url, IEnumerable<string> queryParameters = null)
        {
            var targetUrl = queryParameters == null ? url : BuildQueryString(queryParameters);
            var requestMessage = new HttpRequestMessage(httpMethod, targetUrl);
            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        public void Initialize(int port, string token)
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
        }

        public async Task<string> PostAsync(string url, object body, IEnumerable<string> queryParameters)
        {
            var targetUrl = queryParameters == null ? url : BuildQueryString(queryParameters);
            var responseMessage = await _httpClient.PostAsJsonAsync(targetUrl, body);
            responseMessage.EnsureSuccessStatusCode();
            return await responseMessage.Content.ReadAsStringAsync();
        }

        private static string BuildQueryString(IEnumerable<string> parameters)
        {
            return "?" + string.Join("&", parameters.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        public async Task<string> SendAsync(HttpMethod httpMethod, string url, object body, IEnumerable<string> queryParameters)
        {
            var targetUrl = queryParameters == null ? url : BuildQueryString(queryParameters);
            var requestMessage = new HttpRequestMessage(httpMethod, targetUrl)
            {
                Content = new StringContent(body?.ToString(), Encoding.UTF8, _jsonType)
            };
            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
    }
}
