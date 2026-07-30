using Moq;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LeagueClientTests
    {
        [Fact]
        public async Task StartAsync_WhenClientUnavailable_CanStopAndRestart()
        {
            var clientService = new Mock<IClientService>();
            clientService.Setup(service => service.GetClientProcessId()).Returns(0);
            clientService.Setup(service => service.GetClientCommandLines()).Returns((System.Collections.Generic.Dictionary<string, string>)null);
            var client = new LeagueClient(clientService.Object);

            Assert.False(await client.StartAsync());
            Assert.False(client.Connected);
            await client.StopAsync();

            Assert.False(await client.StartAsync());
            Assert.False(client.Connected);
            await client.StopAsync();
        }
    }
}
