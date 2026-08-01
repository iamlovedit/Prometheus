using Moq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class ClientServiceTests
    {
        [Fact]
        public async Task GetQueuesAsync_WhenCalledTwice_CachesSuccessfulResponse()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            httpService.Setup(service => service.GetAsync<List<GameQueue>>(
                    "lol-game-queues/v1/queues", null, cancellationToken))
                .ReturnsAsync(
                [
                    new GameQueue
                    {
                        Id = 450,
                        ShortName = "极地大乱斗",
                        GameMode = "ARAM"
                    }
                ]);
            var service = new ClientService(httpService.Object);

            var first = await service.GetQueuesAsync(cancellationToken);
            var second = await service.GetQueuesAsync(cancellationToken);

            var queue = Assert.Single(first);
            Assert.Equal("极地大乱斗", queue.DisplayName);
            Assert.Same(first, second);
            httpService.Verify(service => service.GetAsync<List<GameQueue>>(
                "lol-game-queues/v1/queues", null, cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetQueuesAsync_WhenLcuIsUnavailable_ReturnsEmptyList()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<List<GameQueue>>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<GameQueue>)null);
            var service = new ClientService(httpService.Object);

            var queues = await service.GetQueuesAsync();

            Assert.NotNull(queues);
            Assert.Empty(queues);
        }
    }
}
