using Prometheus.Services;
using System.Threading.Tasks;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class HttpServiceTests
    {
        [Fact]
        public async Task Reset_MakesSubsequentRequestsReturnDefault()
        {
            var service = new HttpService();
            service.Initialize(2999, "test-token");

            service.Reset();

            Assert.False(service.IsInitialized);
            Assert.Null(await service.GetAsync("lol-gameflow/v1/gameflow-phase"));
        }
    }
}
