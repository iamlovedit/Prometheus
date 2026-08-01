using Prometheus.Services;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class HttpServiceTests
    {
        [Fact]
        public void Initialize_ConfiguresSixtySecondTimeout()
        {
            var service = new TestHttpService();

            service.Initialize(2999, "test-token");

            Assert.Equal(TimeSpan.FromSeconds(60), service.ClientTimeout);
        }

        [Fact]
        public async Task Reset_MakesSubsequentRequestsReturnDefault()
        {
            var service = new HttpService();
            service.Initialize(2999, "test-token");

            service.Reset();

            Assert.False(service.IsInitialized);
            Assert.Null(await service.GetAsync("lol-gameflow/v1/gameflow-phase"));
        }

        private sealed class TestHttpService : HttpService
        {
            public TimeSpan ClientTimeout => _httpClient.Timeout;
        }
    }
}
