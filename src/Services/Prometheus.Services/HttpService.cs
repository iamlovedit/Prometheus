using Prometheus.Services.Interfaces;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Prometheus.Services
{
    public class HttpService : IHttpService
    {
        private HttpClient _httpClient;

        public Task<string> GetAsync()
        {
            throw new NotImplementedException();
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
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "LeagueOfLegendsClient/12.7.433.4138 (CEF 91)");
            _httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(tokenBytes));
        }
    }
}
