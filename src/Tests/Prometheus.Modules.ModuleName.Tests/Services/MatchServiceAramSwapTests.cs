using Moq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class MatchServiceAramSwapTests
    {
        [Fact]
        public async Task AramBenchSwap_UsesPreferredListOrderInsteadOfBenchOrder()
        {
            var context = CreateContext(
                CreateGameflowSession("ARAM", 450, 12),
                CreateChampionSelect(99, [103, 22]),
                [22, 103]);

            await context.Service.StartAsync();
            var championId = await context.SwapRequested.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            Assert.Equal(22, championId);
            context.GameService.Verify(service =>
                service.SwapAramBenchChampionAsync(
                    22, It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Theory]
        [InlineData("CLASSIC", GameQueueIds.HextechAram, 0)]
        [InlineData("KIWI", GameQueueIds.HextechAramGameflow, 0)]
        [InlineData("KIWI", 0, 0)]
        public async Task AramBenchSwap_InHextechAram_SwapsPreferredChampion(
            string gameMode,
            int queueId,
            int mapId)
        {
            var context = CreateContext(
                CreateGameflowSession(gameMode, queueId, mapId),
                CreateChampionSelect(99, [22]),
                [22]);

            await context.Service.StartAsync();
            var championId = await context.SwapRequested.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            Assert.Equal(22, championId);
            context.GameService.Verify(service =>
                service.SwapAramBenchChampionAsync(
                    22, It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AramBenchSwap_WhenHextechQueueArrivesFromLobby_ReevaluatesAutomation()
        {
            var context = CreateContext(
                CreateGameflowSession("CLASSIC", 0, 0),
                CreateChampionSelect(99, [22]),
                [22]);

            await context.Service.StartAsync();
            var handler = context.Subscriptions["/lol-lobby/v2/lobby"];
            handler(new OnWebsocketEventArgs
            {
                Data = new LobbySnapshot
                {
                    GameConfig = new LobbyGameConfiguration
                    {
                        QueueId = GameQueueIds.HextechAramGameflow,
                        GameMode = "KIWI"
                    }
                },
                EventType = "Update",
                Uri = "/lol-lobby/v2/lobby"
            });

            var championId = await context.SwapRequested.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            Assert.Equal(22, championId);
            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AramBenchSwap_WhenHextechQueueArrivesFromMatchmaking_ReevaluatesAutomation()
        {
            var context = CreateContext(
                CreateGameflowSession("CLASSIC", 0, 0),
                CreateChampionSelect(99, [22]),
                [22]);

            await context.Service.StartAsync();
            var handler = context.Subscriptions["/lol-matchmaking/v1/search"];
            handler(new OnWebsocketEventArgs
            {
                Data = new MatchmakingSnapshot
                {
                    Queue = new MatchmakingQueue
                    {
                        Id = GameQueueIds.HextechAramGameflow
                    }
                },
                EventType = "Update",
                Uri = "/lol-matchmaking/v1/search"
            });

            var championId = await context.SwapRequested.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            Assert.Equal(22, championId);
            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AramBenchSwap_WhenCurrentChampionIsPreferred_DoesNotSwap()
        {
            var context = CreateContext(
                CreateGameflowSession("ARAM", 450, 12),
                CreateChampionSelect(103, [22]),
                [22, 103]);

            await context.Service.StartAsync();
            await Task.Delay(100);

            context.GameService.Verify(service =>
                service.SwapAramBenchChampionAsync(
                    It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AramBenchSwap_InNonAramChampionSelect_DoesNotSwap()
        {
            var context = CreateContext(
                CreateGameflowSession("CLASSIC", 420, 11),
                CreateChampionSelect(99, [22]),
                [22]);

            await context.Service.StartAsync();
            await Task.Delay(100);

            context.GameService.Verify(service =>
                service.SwapAramBenchChampionAsync(
                    It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AramBenchSwap_RepeatedIdenticalBenchEvents_DoNotDuplicateRequest()
        {
            var championSelect = CreateChampionSelect(99, [22]);
            var context = CreateContext(
                CreateGameflowSession("ARAM", 450, 12),
                championSelect,
                [22]);

            await context.Service.StartAsync();
            await context.SwapRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var handler = context.Subscriptions["/lol-champ-select/v1/session"];
            handler(new OnWebsocketEventArgs
            {
                Data = championSelect,
                EventType = "Update",
                Uri = "/lol-champ-select/v1/session"
            });
            handler(new OnWebsocketEventArgs
            {
                Data = championSelect,
                EventType = "Update",
                Uri = "/lol-champ-select/v1/session"
            });
            await Task.Delay(100);

            context.GameService.Verify(service =>
                service.SwapAramBenchChampionAsync(
                    22, It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AramBenchSwap_AfterRetriesFail_AllowsSameBenchStateToRetryLater()
        {
            var championSelect = CreateChampionSelect(99, [22]);
            var context = CreateContext(
                CreateGameflowSession("ARAM", GameQueueIds.Aram, 12),
                championSelect,
                [22]);
            var attempts = 0;
            var retriesExhausted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var retried = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.GameService.Setup(service =>
                    service.SwapAramBenchChampionAsync(
                        22, It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    var attempt = Interlocked.Increment(ref attempts);
                    if (attempt == 3)
                    {
                        retriesExhausted.TrySetResult();
                    }
                    else if (attempt == 4)
                    {
                        retried.TrySetResult();
                    }
                })
                .ThrowsAsync(new HttpRequestException("LCU temporarily unavailable"));

            await context.Service.StartAsync();
            await retriesExhausted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(100);

            var handler = context.Subscriptions["/lol-champ-select/v1/session"];
            handler(new OnWebsocketEventArgs
            {
                Data = championSelect,
                EventType = "Update",
                Uri = "/lol-champ-select/v1/session"
            });

            await retried.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(Volatile.Read(ref attempts) >= 4);

            await context.Service.StopAsync();
        }

        private static TestContext CreateContext(
            GameflowSessionSnapshot gameflowSession,
            ChampionSelectSnapshot championSelect,
            IReadOnlyList<int> preferredChampionIds)
        {
            var context = new TestContext();
            context.LeagueClient.SetupGet(client => client.Connected).Returns(true);
            context.LeagueClient.SetupGet(client => client.Port).Returns("2999");
            context.LeagueClient.SetupGet(client => client.Token).Returns("test-token");
            context.LeagueClient.SetupGet(client => client.ProcessId).Returns(1234);
            context.LeagueClient.Setup(client =>
                    client.StartAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            context.LeagueClient.Setup(client =>
                    client.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            context.LeagueClient.Setup(client => client.Subscribe(
                    It.IsAny<string>(), It.IsAny<Action<OnWebsocketEventArgs>>()))
                .Callback<string, Action<OnWebsocketEventArgs>>((uri, handler) =>
                    context.Subscriptions[uri] = handler);

            context.HttpService.SetupGet(service => service.IsInitialized)
                .Returns(() => context.HttpInitialized);
            context.HttpService.Setup(service =>
                    service.Initialize(It.IsAny<int>(), It.IsAny<string>()))
                .Callback(() => context.HttpInitialized = true);
            context.HttpService.Setup(service => service.Reset())
                .Callback(() => context.HttpInitialized = false);

            context.GameService.Setup(service =>
                    service.GetGameflowPhaseAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("ChampSelect");
            context.GameService.Setup(service =>
                    service.GetGameflowSessionSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(gameflowSession);
            context.GameService.Setup(service =>
                    service.GetLobbySnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((LobbySnapshot)null);
            context.GameService.Setup(service =>
                    service.GetMatchmakingSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((MatchmakingSnapshot)null);
            context.GameService.Setup(service =>
                    service.GetReadyCheckSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ReadyCheckSnapshot)null);
            context.GameService.Setup(service =>
                    service.GetChampionSelectSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(championSelect);
            context.GameService.Setup(service =>
                    service.GetPostGameSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((PostGameSnapshot)null);
            context.GameService.Setup(service =>
                    service.SwapAramBenchChampionAsync(
                        It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<int, CancellationToken>((championId, _) =>
                    context.SwapRequested.TrySetResult(championId))
                .Returns(Task.CompletedTask);

            context.SummonerService.Setup(service =>
                    service.GetCurrentSummoner(It.IsAny<CancellationToken>()))
                .ReturnsAsync((SummonerAccount)null);

            context.AutomationSettings.SetupGet(settings =>
                settings.AutoAcceptReadyCheck).Returns(false);
            context.AutomationSettings.SetupGet(settings =>
                settings.AutoReconnect).Returns(false);
            context.AutomationSettings.SetupGet(settings =>
                settings.AutoSwapAramBench).Returns(true);
            context.AutomationSettings.SetupGet(settings =>
                settings.PreferredAramChampionIds).Returns(preferredChampionIds);

            context.Service = new MatchService(
                context.LeagueClient.Object,
                context.HttpService.Object,
                context.GameService.Object,
                context.SummonerService.Object,
                context.GameResourceManager.Object,
                context.AutomationSettings.Object);
            return context;
        }

        private static GameflowSessionSnapshot CreateGameflowSession(
            string gameMode,
            int queueId,
            int mapId)
        {
            return new GameflowSessionSnapshot
            {
                Phase = "ChampSelect",
                GameData = new GameflowGameData
                {
                    GameMode = gameMode,
                    QueueId = queueId,
                    MapId = mapId
                }
            };
        }

        private static ChampionSelectSnapshot CreateChampionSelect(
            int currentChampionId,
            IReadOnlyList<int> benchChampionIds)
        {
            return new ChampionSelectSnapshot
            {
                BenchEnabled = true,
                LocalPlayerCellId = 1,
                MyTeam =
                [
                    new ChampionSelectTeamMemberSnapshot
                    {
                        CellId = 1,
                        ChampionId = currentChampionId
                    }
                ],
                BenchChampions = benchChampionIds
                    .Select(championId => new ChampionSelectBenchChampionSnapshot
                    {
                        ChampionId = championId
                    })
                    .ToList()
            };
        }

        private sealed class TestContext
        {
            public Mock<ILeagueClient> LeagueClient { get; } = new();

            public Mock<IHttpService> HttpService { get; } = new();

            public Mock<IGameService> GameService { get; } = new();

            public Mock<ISummonerService> SummonerService { get; } = new();

            public Mock<IGameResourceManager> GameResourceManager { get; } = new();

            public Mock<IGameAutomationSettings> AutomationSettings { get; } = new();

            public Dictionary<string, Action<OnWebsocketEventArgs>> Subscriptions { get; } = [];

            public TaskCompletionSource<int> SwapRequested { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public MatchService Service { get; set; }

            public bool HttpInitialized { get; set; }
        }
    }
}
