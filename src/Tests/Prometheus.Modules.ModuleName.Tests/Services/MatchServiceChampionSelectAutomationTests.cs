using Moq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class MatchServiceChampionSelectAutomationTests
    {
        [Fact]
        public async Task AutoPick_UsesFirstAvailablePreferredChampion()
        {
            var context = CreateContext(
                "pick",
                preferredPickIds: [103, 22],
                pickableIds: [22, 103]);

            await context.Service.StartAsync();
            var request = await context.ActionRequested.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            Assert.Equal(17, request.ActionId);
            Assert.Equal(103, request.ChampionId);
            context.GameService.Verify(service =>
                service.CompleteChampionSelectActionAsync(
                    It.Is<ChampionSelectActionSnapshot>(action => action.Id == 17),
                    103,
                    It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AutoBan_SkipsAllyPickIntentAndUsesFallback()
        {
            var context = CreateContext(
                "ban",
                preferredBanIds: [103, 22],
                bannableIds: [103, 22],
                allyPickIntent: 103);

            await context.Service.StartAsync();
            var request = await context.ActionRequested.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            Assert.Equal(22, request.ChampionId);
            context.GameService.Verify(service =>
                service.CompleteChampionSelectActionAsync(
                    It.IsAny<ChampionSelectActionSnapshot>(),
                    22,
                    It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task RepeatedIdenticalActionEvents_DoNotDuplicateRequest()
        {
            var context = CreateContext(
                "pick",
                preferredPickIds: [103],
                pickableIds: [103]);

            await context.Service.StartAsync();
            await context.ActionRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var handler = context.Subscriptions["/lol-champ-select/v1/session"];
            handler(new OnWebsocketEventArgs
            {
                Data = context.ChampionSelect,
                EventType = "Update",
                Uri = "/lol-champ-select/v1/session"
            });
            handler(new OnWebsocketEventArgs
            {
                Data = context.ChampionSelect,
                EventType = "Update",
                Uri = "/lol-champ-select/v1/session"
            });
            await Task.Delay(100);

            context.GameService.Verify(service =>
                service.CompleteChampionSelectActionAsync(
                    It.IsAny<ChampionSelectActionSnapshot>(),
                    103,
                    It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        private static TestContext CreateContext(
            string actionType,
            IReadOnlyList<int> preferredPickIds = null,
            IReadOnlyList<int> preferredBanIds = null,
            IReadOnlyList<int> pickableIds = null,
            IReadOnlyList<int> bannableIds = null,
            int allyPickIntent = 0)
        {
            var context = new TestContext
            {
                ChampionSelect = CreateChampionSelect(actionType, allyPickIntent)
            };
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
                .ReturnsAsync(new GameflowSessionSnapshot
                {
                    Phase = "ChampSelect",
                    GameData = new GameflowGameData
                    {
                        GameMode = "CLASSIC",
                        QueueId = 420,
                        MapId = 11
                    }
                });
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
                .ReturnsAsync(context.ChampionSelect);
            context.GameService.Setup(service =>
                    service.GetPostGameSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((PostGameSnapshot)null);
            context.GameService.Setup(service =>
                    service.GetPickableChampionIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(pickableIds ?? []);
            context.GameService.Setup(service =>
                    service.GetBannableChampionIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(bannableIds ?? []);
            context.GameService.Setup(service =>
                    service.CompleteChampionSelectActionAsync(
                        It.IsAny<ChampionSelectActionSnapshot>(),
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                .Callback<ChampionSelectActionSnapshot, int, CancellationToken>(
                    (action, championId, _) =>
                        context.ActionRequested.TrySetResult(
                            new ActionRequest(action.Id, championId)))
                .Returns(Task.CompletedTask);

            context.SummonerService.Setup(service =>
                    service.GetCurrentSummoner(It.IsAny<CancellationToken>()))
                .ReturnsAsync((SummonerAccount)null);

            context.AutomationSettings.SetupGet(settings =>
                settings.AutoAcceptReadyCheck).Returns(false);
            context.AutomationSettings.SetupGet(settings =>
                settings.AutoReconnect).Returns(false);
            context.AutomationSettings.SetupGet(settings =>
                settings.AutoSwapAramBench).Returns(false);
            context.AutomationSettings.SetupGet(settings =>
                settings.AutoPickChampion).Returns(actionType == "pick");
            context.AutomationSettings.SetupGet(settings =>
                settings.AutoBanChampion).Returns(actionType == "ban");
            context.AutomationSettings.SetupGet(settings =>
                settings.PreferredPickChampionIds).Returns(preferredPickIds ?? []);
            context.AutomationSettings.SetupGet(settings =>
                settings.PreferredBanChampionIds).Returns(preferredBanIds ?? []);

            context.Service = new MatchService(
                context.LeagueClient.Object,
                context.HttpService.Object,
                context.GameService.Object,
                context.SummonerService.Object,
                context.GameResourceManager.Object,
                context.AutomationSettings.Object);
            return context;
        }

        private static ChampionSelectSnapshot CreateChampionSelect(
            string actionType,
            int allyPickIntent)
        {
            return new ChampionSelectSnapshot
            {
                LocalPlayerCellId = 1,
                Actions =
                [
                    [
                        new ChampionSelectActionSnapshot
                        {
                            Id = 17,
                            ActorCellId = 1,
                            Type = actionType,
                            IsAllyAction = true,
                            IsInProgress = true,
                            Completed = false,
                            PickTurn = 1
                        }
                    ]
                ],
                MyTeam =
                [
                    new ChampionSelectTeamMemberSnapshot
                    {
                        CellId = 1
                    },
                    new ChampionSelectTeamMemberSnapshot
                    {
                        CellId = 2,
                        ChampionPickIntent = allyPickIntent
                    }
                ]
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

            public TaskCompletionSource<ActionRequest> ActionRequested { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public ChampionSelectSnapshot ChampionSelect { get; set; }

            public MatchService Service { get; set; }

            public bool HttpInitialized { get; set; }
        }

        private readonly record struct ActionRequest(int ActionId, int ChampionId);
    }
}
