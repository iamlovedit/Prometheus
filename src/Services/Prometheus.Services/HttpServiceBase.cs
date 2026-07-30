using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Services
{
    public abstract class HttpServiceBase
    {
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
                Timeout = TimeSpan.FromSeconds(10)
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

            oldClient?.Dispose();
        }

        protected virtual async Task<HttpResponseMessage> GetHttpMessageAsync(string url,
            IEnumerable<string> queryParameters, CancellationToken cancellationToken)
        {
            var request = CreateRequestMessage(HttpMethod.Get, url, queryParameters);
            var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return response;
        }

        protected virtual async Task<HttpResponseMessage> PostHttpMessageAsync(string url, object body,
            IEnumerable<string> queryParameters, CancellationToken cancellationToken)
        {
            var request = CreateRequestMessage(HttpMethod.Post, url, queryParameters, body, true);
            var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return response;
        }

        protected virtual async Task<HttpResponseMessage> SendHttpMessageAsync(HttpMethod httpMethod,
            string url, object body, IEnumerable<string> queryParameters,
            CancellationToken cancellationToken)
        {
            var request = CreateRequestMessage(httpMethod, url, queryParameters, body, true);
            var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return response;
        }

        protected HttpRequestMessage CreateRequestMessage(HttpMethod httpMethod, string url,
            IEnumerable<string> queryParameters = null, object body = null, bool includeBody = false)
        {
            if (!_isInitialized || _httpClient is null)
            {
                throw new InvalidOperationException("The HTTP service has not been initialized.");
            }

            var target = BuildRelativeUrl(url, queryParameters);
            var request = new HttpRequestMessage(httpMethod, target);
            if (includeBody)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, _jsonType);
            }

            var effectiveUri = ResolveEffectiveUri(target);
            if (ShouldAuthenticate(effectiveUri))
            {
                request.Headers.Authorization = _authorization;
            }

            return request;
        }

        private Uri ResolveEffectiveUri(string target)
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri;
            }

            return new Uri(_authenticatedBaseAddress, target);
        }

        private bool ShouldAuthenticate(Uri requestUri)
        {
            if (requestUri is null || _authenticatedBaseAddress is null || !requestUri.IsLoopback)
            {
                return false;
            }

            return string.Equals(requestUri.Scheme, _authenticatedBaseAddress.Scheme,
                       StringComparison.OrdinalIgnoreCase)
                   && requestUri.Port == _authenticatedBaseAddress.Port;
        }
    }
}
