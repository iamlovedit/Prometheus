using Moq;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
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
            var gameService = new GameService(httpService.Object);

            await gameService.SwapAramBenchChampionAsync(103, cancellationToken);

            httpService.Verify(service => service.PostAsync(
                "lol-champ-select/v1/session/bench/swap/103",
                null,
                cancellationToken), Times.Once);
        }

        [Fact]
        public async Task SwapAramBenchChampionAsync_WithInvalidId_Throws()
        {
            var gameService = new GameService(new Mock<IHttpService>().Object);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                gameService.SwapAramBenchChampionAsync(0));
        }
    }
}
