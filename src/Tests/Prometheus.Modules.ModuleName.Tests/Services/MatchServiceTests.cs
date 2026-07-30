using Moq;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class MatchServiceTests
    {
        [Fact]
        public async Task Disconnect_ResetsHttpAndPublishesReconnecting()
        {
            var context = CreateContext();
            await context.Service.StartAsync();

            context.Connected = false;
            context.LeagueClient.Raise(client => client.OnDisconnected += null);

            Assert.Equal(ConnectionState.Reconnecting, context.Service.Current.ConnectionState);
            Assert.False(context.HttpInitialized);
            context.HttpService.Verify(service => service.Reset(), Times.Once);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task Reconnect_ReinitializesHttpAndPublishesConnected()
        {
            var context = CreateContext();
            await context.Service.StartAsync();

            context.Connected = false;
            context.LeagueClient.Raise(client => client.OnDisconnected += null);

            var reinitialized = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.Service.SnapshotChanged += (_, args) =>
            {
                if (context.HttpInitializeCount == 2 &&
                    args.Snapshot.ConnectionState == ConnectionState.Connected)
                {
                    reinitialized.TrySetResult(true);
                }
            };

            context.Connected = true;
            context.LeagueClient.Raise(client => client.OnConnected += null);

            await reinitialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(ConnectionState.Connected, context.Service.Current.ConnectionState);
            Assert.True(context.HttpInitialized);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task LatePhaseEventAfterDisconnect_DoesNotIssueHttpRefresh()
        {
            var context = CreateContext();
            await context.Service.StartAsync();

            context.Connected = false;
            context.LeagueClient.Raise(client => client.OnDisconnected += null);
            context.GameService.Invocations.Clear();

            context.Subscriptions["/lol-gameflow/v1/gameflow-phase"](
                new OnWebsocketEventArgs
                {
                    Data = "Lobby",
                    EventType = "Update",
                    Uri = "/lol-gameflow/v1/gameflow-phase"
                });

            context.GameService.Verify(service =>
                service.GetGameflowSessionSnapshotAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal(ConnectionState.Reconnecting, context.Service.Current.ConnectionState);

            await context.Service.StopAsync();
        }

        private static TestContext CreateContext()
        {
            var context = new TestContext();

            context.LeagueClient.SetupGet(client => client.Connected)
                .Returns(() => context.Connected);
            context.LeagueClient.SetupGet(client => client.Port).Returns("2999");
            context.LeagueClient.SetupGet(client => client.Token).Returns("test-token");
            context.LeagueClient.SetupGet(client => client.ProcessId).Returns(1234);
            context.LeagueClient.Setup(client => client.StartAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            context.LeagueClient.Setup(client => client.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            context.LeagueClient.Setup(client =>
                    client.Subscribe(It.IsAny<string>(), It.IsAny<Action<OnWebsocketEventArgs>>()))
                .Callback<string, Action<OnWebsocketEventArgs>>((uri, handler) =>
                    context.Subscriptions[uri] = handler);

            context.HttpService.SetupGet(service => service.IsInitialized)
                .Returns(() => context.HttpInitialized);
            context.HttpService.Setup(service => service.Initialize(It.IsAny<int>(), It.IsAny<string>()))
                .Callback(() =>
                {
                    context.HttpInitialized = true;
                    context.HttpInitializeCount++;
                });
            context.HttpService.Setup(service => service.Reset())
                .Callback(() => context.HttpInitialized = false);

            context.GameService.Setup(service =>
                    service.GetGameflowPhaseAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("None");
            context.GameService.Setup(service =>
                    service.GetGameflowSessionSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((GameflowSessionSnapshot)null);
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
                .ReturnsAsync((ChampionSelectSnapshot)null);
            context.GameService.Setup(service =>
                    service.GetPostGameSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((PostGameSnapshot)null);

            context.AutomationSettings.SetupGet(settings => settings.AutoAcceptReadyCheck)
                .Returns(false);
            context.AutomationSettings.SetupGet(settings => settings.AutoReconnect)
                .Returns(false);

            context.Service = new MatchService(context.LeagueClient.Object,
                context.HttpService.Object, context.GameService.Object,
                context.SummonerService.Object, context.GameResourceManager.Object,
                context.AutomationSettings.Object);
            return context;
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

            public MatchService Service { get; set; }

            public bool Connected { get; set; } = true;

            public bool HttpInitialized { get; set; }

            public int HttpInitializeCount { get; set; }

        }
    }
}
