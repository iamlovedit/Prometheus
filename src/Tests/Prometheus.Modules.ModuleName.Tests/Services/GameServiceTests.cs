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

        [Fact]
        public async Task CreateMatchmadeLobbyAsync_WhenQueueIsAvailable_PostsQueueAndConfirmsLobby()
        {
            var httpService = new Mock<IHttpService>();
            var clientService = new Mock<IClientService>();
            var cancellationToken = new CancellationTokenSource().Token;
            httpService.SetupGet(service => service.IsInitialized).Returns(true);
            clientService.Setup(service => service.GetQueuesAsync(cancellationToken))
                .ReturnsAsync(
                [
                    new GameQueue
                    {
                        Id = GameQueueIds.RankedSoloDuo,
                        IsEnabled = true,
                        QueueAvailability = "Available"
                    }
                ]);
            httpService.Setup(service => service.PostAsync<LobbySnapshot>(
                    "lol-lobby/v2/lobby",
                    It.Is<object>(body => ReadQueueId(body) == GameQueueIds.RankedSoloDuo),
                    null,
                    cancellationToken))
                .ReturnsAsync(new LobbySnapshot
                {
                    GameConfig = new LobbyGameConfiguration
                    {
                        QueueId = GameQueueIds.RankedSoloDuo
                    }
                });
            var gameService = new GameService(httpService.Object, clientService.Object);

            var result = await gameService.CreateMatchmadeLobbyAsync(
                GameQueueIds.RankedSoloDuo,
                cancellationToken);

            Assert.True(result.Succeeded);
            Assert.Equal(MatchmadeLobbyCreationStatus.Created, result.Status);
            Assert.Equal(GameQueueIds.RankedSoloDuo, result.Lobby.GameConfig.QueueId);
            httpService.Verify(service => service.PostAsync<LobbySnapshot>(
                "lol-lobby/v2/lobby",
                It.Is<object>(body => ReadQueueId(body) == GameQueueIds.RankedSoloDuo),
                null,
                cancellationToken), Times.Once);
        }

        [Fact]
        public async Task CreateMatchmadeLobbyAsync_WhenPostResponseIsEmpty_ConfirmsWithLobbyQuery()
        {
            var httpService = new Mock<IHttpService>();
            var clientService = new Mock<IClientService>();
            httpService.SetupGet(service => service.IsInitialized).Returns(true);
            clientService.Setup(service => service.GetQueuesAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new GameQueue
                    {
                        Id = GameQueueIds.Aram,
                        IsEnabled = true,
                        QueueAvailability = "Available"
                    }
                ]);
            httpService.Setup(service => service.PostAsync<LobbySnapshot>(
                    "lol-lobby/v2/lobby",
                    It.IsAny<object>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((LobbySnapshot)null);
            httpService.Setup(service => service.GetAsync<LobbySnapshot>(
                    "lol-lobby/v2/lobby",
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LobbySnapshot
                {
                    GameConfig = new LobbyGameConfiguration
                    {
                        QueueId = GameQueueIds.Aram
                    }
                });
            var gameService = new GameService(httpService.Object, clientService.Object);

            var result = await gameService.CreateMatchmadeLobbyAsync(GameQueueIds.Aram);

            Assert.Equal(MatchmadeLobbyCreationStatus.Created, result.Status);
            httpService.Verify(service => service.GetAsync<LobbySnapshot>(
                "lol-lobby/v2/lobby",
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateMatchmadeLobbyAsync_WhenClientIsUnavailable_DoesNotSendRequest()
        {
            var httpService = new Mock<IHttpService>();
            var clientService = new Mock<IClientService>();
            httpService.SetupGet(service => service.IsInitialized).Returns(false);
            var gameService = new GameService(httpService.Object, clientService.Object);

            var result = await gameService.CreateMatchmadeLobbyAsync(
                GameQueueIds.RankedFlex);

            Assert.Equal(MatchmadeLobbyCreationStatus.ClientUnavailable, result.Status);
            clientService.Verify(service => service.GetQueuesAsync(
                It.IsAny<CancellationToken>()), Times.Never);
            httpService.Verify(service => service.PostAsync<LobbySnapshot>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateMatchmadeLobbyAsync_WhenQueueIsDisabled_DoesNotSendRequest()
        {
            var httpService = new Mock<IHttpService>();
            var clientService = new Mock<IClientService>();
            httpService.SetupGet(service => service.IsInitialized).Returns(true);
            clientService.Setup(service => service.GetQueuesAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new GameQueue
                    {
                        Id = GameQueueIds.HextechAram,
                        IsEnabled = false,
                        QueueAvailability = "PlatformDisabled"
                    }
                ]);
            var gameService = new GameService(httpService.Object, clientService.Object);

            var result = await gameService.CreateMatchmadeLobbyAsync(
                GameQueueIds.HextechAram);

            Assert.Equal(MatchmadeLobbyCreationStatus.QueueUnavailable, result.Status);
            httpService.Verify(service => service.PostAsync<LobbySnapshot>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateMatchmadeLobbyAsync_WhenAnotherCreationIsRunning_RejectsDuplicate()
        {
            var httpService = new Mock<IHttpService>();
            var clientService = new Mock<IClientService>();
            var postStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var completePost = new TaskCompletionSource<LobbySnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            httpService.SetupGet(service => service.IsInitialized).Returns(true);
            clientService.Setup(service => service.GetQueuesAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new GameQueue
                    {
                        Id = GameQueueIds.RankedSoloDuo,
                        IsEnabled = true,
                        QueueAvailability = "Available"
                    }
                ]);
            httpService.Setup(service => service.PostAsync<LobbySnapshot>(
                    "lol-lobby/v2/lobby",
                    It.IsAny<object>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .Callback(() => postStarted.TrySetResult())
                .Returns(() => completePost.Task);
            var gameService = new GameService(
                httpService.Object, clientService.Object);

            var firstCreation = gameService.CreateMatchmadeLobbyAsync(
                GameQueueIds.RankedSoloDuo);
            await postStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var duplicate = await gameService.CreateMatchmadeLobbyAsync(
                GameQueueIds.RankedSoloDuo);

            Assert.Equal(MatchmadeLobbyCreationStatus.OperationInProgress,
                duplicate.Status);
            httpService.Verify(service => service.PostAsync<LobbySnapshot>(
                "lol-lobby/v2/lobby",
                It.IsAny<object>(),
                null,
                It.IsAny<CancellationToken>()), Times.Once);

            completePost.SetResult(new LobbySnapshot
            {
                GameConfig = new LobbyGameConfiguration
                {
                    QueueId = GameQueueIds.RankedSoloDuo
                }
            });
            Assert.True((await firstCreation).Succeeded);
        }

        private static int ReadQueueId(object body)
        {
            var property = body?.GetType().GetProperty("queueId");
            return property?.GetValue(body) is int queueId ? queueId : 0;
        }
    }
}
