using Moq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class GameServiceTests
    {
        [Fact]
        public async Task SwapAramBenchChampionAsync_PostsChampionSpecificEndpoint()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            httpService.Setup(service => service.PostAsync(
                    "lol-champ-select/v1/session/bench/swap/103",
                    null,
                    cancellationToken))
                .Returns(Task.CompletedTask);
            var gameService = new GameService(
                httpService.Object, new Mock<IClientService>().Object);

            await gameService.SwapAramBenchChampionAsync(103, cancellationToken);

            httpService.Verify(service => service.PostAsync(
                "lol-champ-select/v1/session/bench/swap/103",
                null,
                cancellationToken), Times.Once);
        }

        [Fact]
        public async Task SwapAramBenchChampionAsync_WithInvalidId_Throws()
        {
            var gameService = new GameService(
                new Mock<IHttpService>().Object, new Mock<IClientService>().Object);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                gameService.SwapAramBenchChampionAsync(0));
        }

        [Fact]
        public async Task GetMatchDetailAsync_WhenQueueMetadataIsAvailable_ResolvesDisplayMode()
        {
            var httpService = new Mock<IHttpService>();
            var clientService = new Mock<IClientService>();
            var cancellationToken = new CancellationTokenSource().Token;
            httpService.Setup(service => service.GetAsync<MatchDetail>(
                    "lol-match-history/v1/games/12345", null, cancellationToken))
                .ReturnsAsync(new MatchDetail
                {
                    GameId = 12345,
                    QueueId = 420,
                    GameMode = "CLASSIC"
                });
            clientService.Setup(service => service.GetQueuesAsync(cancellationToken))
                .ReturnsAsync(
                [
                    new GameQueue
                    {
                        Id = 420,
                        ShortName = "单双排位"
                    }
                ]);
            var gameService = new GameService(httpService.Object, clientService.Object);

            var match = await gameService.GetMatchDetailAsync(12345, cancellationToken);

            Assert.Equal("单双排位", match.DisplayGameMode);
            httpService.Verify(service => service.GetAsync<MatchDetail>(
                "lol-match-history/v1/games/12345", null, cancellationToken), Times.Once);
            clientService.Verify(service => service.GetQueuesAsync(cancellationToken), Times.Once);
        }
    }
}
