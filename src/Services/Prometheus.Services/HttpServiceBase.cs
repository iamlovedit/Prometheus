using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;

namespace Prometheus.Services
{
    public abstract class HttpServiceBase
    {
        private static readonly TimeSpan RetiredClientLifetime = TimeSpan.FromSeconds(11);

        private readonly object _initializationSync = new();
        private AuthenticationHeaderValue _authorization;
        private Uri _authenticatedBaseAddress;

        protected HttpClient _httpClient;

        protected volatile bool _isInitialized;

        protected readonly string _jsonType = "application/json";

        public bool IsInitialized => _isInitialized;

        protected virtual string BuildQueryStringFromParameters(IEnumerable<string> queryParameters)
        {
            var values = queryParameters?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
                ?? Array.Empty<string>();
            return values.Length == 0 ? string.Empty : string.Join("&", values);
        }

        protected virtual string BuildRelativeUrl(string url, IEnumerable<string> queryParameters)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("A request URL is required.", nameof(url));
            }

            var query = BuildQueryStringFromParameters(queryParameters);
            if (string.IsNullOrEmpty(query))
            {
                return url;
            }

            return url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query;
        }

        public virtual void Initialize(int port, string token)
        {
            if (port is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("An LCU authentication token is required.", nameof(token));
            }

            var baseAddress = new Uri($"https://127.0.0.1:{port}/", UriKind.Absolute);
            var tokenBytes = Encoding.ASCII.GetBytes($"riot:{token}");
            var authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(tokenBytes));

            var httpClientHandler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                // The LCU uses a self-signed certificate.  Only relax
                // validation for loopback requests; public HTTPS keeps normal
                // certificate validation.
                ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
                    request?.RequestUri?.IsLoopback == true || errors == SslPolicyErrors.None
            };

            var client = new HttpClient(httpClientHandler)
            {
                BaseAddress = baseAddress,
                DefaultRequestVersion = new Version(2, 0),
                Timeout = TimeSpan.FromSeconds(60)
            };
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(_jsonType));
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "LeagueOfLegendsClient/12.7.433.4138 (CEF 91)");
            client.DefaultRequestHeaders.Connection.Add("keep-alive");

            HttpClient oldClient;
            lock (_initializationSync)
            {
                oldClient = _httpClient;
                _httpClient = client;
                _authenticatedBaseAddress = baseAddress;
                _authorization = authorization;
                _isInitialized = true;
            }

            RetireClient(oldClient);
        }

        public virtual void Reset()
        {
            HttpClient oldClient;
            lock (_initializationSync)
            {
                oldClient = _httpClient;
                _httpClient = null;
                _authenticatedBaseAddress = null;
                _authorization = null;
                _isInitialized = false;
            }

            RetireClient(oldClient);
        }

        protected virtual async Task<HttpResponseMessage> GetHttpMessageAsync(string url,
            IEnumerable<string> queryParameters, CancellationToken cancellationToken)
        {
            if (!TryCreateRequestMessage(HttpMethod.Get, url, queryParameters, null, false,
                    out var client, out var request))
            {
                return null;
            }

            using (request)
            {
                var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return response;
            }
        }

        protected virtual async Task<HttpResponseMessage> PostHttpMessageAsync(string url, object body,
            IEnumerable<string> queryParameters, CancellationToken cancellationToken)
        {
            if (!TryCreateRequestMessage(HttpMethod.Post, url, queryParameters, body, true,
                    out var client, out var request))
            {
                return null;
            }

            using (request)
            {
                var response = await client.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return response;
            }
        }

        protected virtual async Task<HttpResponseMessage> SendHttpMessageAsync(HttpMethod httpMethod,
            string url, object body, IEnumerable<string> queryParameters,
            CancellationToken cancellationToken)
        {
            if (!TryCreateRequestMessage(httpMethod, url, queryParameters, body, true,
                    out var client, out var request))
            {
                return null;
            }

            using (request)
            {
                var response = await client.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return response;
            }
        }

        protected bool TryCreateRequestMessage(HttpMethod httpMethod, string url,
            IEnumerable<string> queryParameters, object body, bool includeBody,
            out HttpClient client, out HttpRequestMessage request)
        {
            lock (_initializationSync)
            {
                if (!_isInitialized || _httpClient is null)
                {
                    client = null;
                    request = null;
                    return false;
                }

                client = _httpClient;
                var baseAddress = _authenticatedBaseAddress;
                var authorization = _authorization;
                var target = BuildRelativeUrl(url, queryParameters);
                request = new HttpRequestMessage(httpMethod, target);
                if (includeBody)
                {
                    request.Content = new StringContent(
                        JsonConvert.SerializeObject(body), Encoding.UTF8, _jsonType);
                }

                var effectiveUri = ResolveEffectiveUri(target, baseAddress);
                if (ShouldAuthenticate(effectiveUri, baseAddress))
                {
                    request.Headers.Authorization = authorization;
                }

                return true;
            }
        }

        private static Uri ResolveEffectiveUri(string target, Uri baseAddress)
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri;
            }

            return new Uri(baseAddress, target);
        }

        private static bool ShouldAuthenticate(Uri requestUri, Uri authenticatedBaseAddress)
        {
            if (requestUri is null || authenticatedBaseAddress is null || !requestUri.IsLoopback)
            {
                return false;
            }

            return string.Equals(requestUri.Scheme, authenticatedBaseAddress.Scheme,
                       StringComparison.OrdinalIgnoreCase)
                   && requestUri.Port == authenticatedBaseAddress.Port;
        }

        private static void RetireClient(HttpClient client)
        {
            if (client is null)
            {
                return;
            }

            _ = DisposeRetiredClientAsync(client);
        }

        private static async Task DisposeRetiredClientAsync(HttpClient client)
        {
            await Task.Delay(RetiredClientLifetime).ConfigureAwait(false);
            client.Dispose();
        }
    }
}
