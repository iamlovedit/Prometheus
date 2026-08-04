using Moq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class MatchServiceAutoReconnectTests
    {
        [Fact]
        public async Task AutoReconnect_WhenGameClientIsRunning_ReconnectsOnce()
        {
            var context = CreateContext(
                "Reconnect",
                CreateGameflowSession("Reconnect", running: true));

            await context.Service.StartAsync();
            await context.ReconnectRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

            context.GameService.Verify(service => service.ReconnectGameAsync(
                It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AutoReconnect_WhenGameClientIsNotRunning_DoesNotReconnect()
        {
            var context = CreateContext(
                "Reconnect",
                CreateGameflowSession("Reconnect", running: false));

            await context.Service.StartAsync();
            await Task.Delay(100);

            context.GameService.Verify(service => service.ReconnectGameAsync(
                It.IsAny<CancellationToken>()), Times.Never);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AutoReconnect_WhenPhaseEventPrecedesCurrentSession_WaitsForReconnectSession()
        {
            var context = CreateContext(
                "InProgress",
                CreateGameflowSession("InProgress", running: true));

            await context.Service.StartAsync();
            context.Subscriptions["/lol-gameflow/v1/gameflow-phase"](
                new OnWebsocketEventArgs
                {
                    Data = "Reconnect",
                    EventType = "Update",
                    Uri = "/lol-gameflow/v1/gameflow-phase"
                });
            await Task.Delay(100);

            context.GameService.Verify(service => service.ReconnectGameAsync(
                It.IsAny<CancellationToken>()), Times.Never);

            context.Phase = "Reconnect";
            context.GameflowSession = CreateGameflowSession("Reconnect", running: true);
            context.Subscriptions["/lol-gameflow/v1/session"](
                new OnWebsocketEventArgs
                {
                    Data = context.GameflowSession,
                    EventType = "Update",
                    Uri = "/lol-gameflow/v1/session"
                });
            await context.ReconnectRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

            context.GameService.Verify(service => service.ReconnectGameAsync(
                It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AutoReconnect_WhenGameClientStops_CancelsCurrentAttempt()
        {
            var context = CreateContext(
                "Reconnect",
                CreateGameflowSession("Reconnect", running: true));
            var cancellationObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.GameService.Setup(service => service.ReconnectGameAsync(
                    It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(cancellationToken =>
                {
                    context.ReconnectRequested.TrySetResult(true);
                    cancellationToken.Register(() =>
                        cancellationObserved.TrySetResult(true));
                    return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                });

            await context.Service.StartAsync();
            await context.ReconnectRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

            context.GameflowSession = CreateGameflowSession("Reconnect", running: false);
            context.Subscriptions["/lol-gameflow/v1/session"](
                new OnWebsocketEventArgs
                {
                    Data = context.GameflowSession,
                    EventType = "Update",
                    Uri = "/lol-gameflow/v1/session"
                });
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            context.GameService.Verify(service => service.ReconnectGameAsync(
                It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AutoReconnect_WhenHttpPhaseShowsGameEnded_DoesNotPostReconnect()
        {
            var context = CreateContext(
                "Reconnect",
                CreateGameflowSession("Reconnect", running: true));
            var phaseRequestCount = 0;
            var confirmationRequested = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.GameService.Setup(service => service.GetGameflowPhaseAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (Interlocked.Increment(ref phaseRequestCount) == 1)
                    {
                        return "Reconnect";
                    }

                    confirmationRequested.TrySetResult(true);
                    return "EndOfGame";
                });

            await context.Service.StartAsync();
            await confirmationRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(50);

            context.GameService.Verify(service => service.ReconnectGameAsync(
                It.IsAny<CancellationToken>()), Times.Never);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task AutoReconnect_WhenHttpSessionShowsGameClientStopped_DoesNotPostReconnect()
        {
            var context = CreateContext(
                "Reconnect",
                CreateGameflowSession("Reconnect", running: true));
            var sessionRequestCount = 0;
            var confirmationRequested = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.GameService.Setup(service => service.GetGameflowSessionSnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (Interlocked.Increment(ref sessionRequestCount) == 1)
                    {
                        return context.GameflowSession;
                    }

                    confirmationRequested.TrySetResult(true);
                    return CreateGameflowSession("Reconnect", running: false);
                });

            await context.Service.StartAsync();
            await confirmationRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(50);

            context.GameService.Verify(service => service.ReconnectGameAsync(
                It.IsAny<CancellationToken>()), Times.Never);

            await context.Service.StopAsync();
        }

        private static TestContext CreateContext(
            string phase,
            GameflowSessionSnapshot gameflowSession)
        {
            var context = new TestContext
            {
                Phase = phase,
                GameflowSession = gameflowSession
            };
            context.LeagueClient.SetupGet(client => client.Connected).Returns(true);
            context.LeagueClient.SetupGet(client => client.Port).Returns("2999");
            context.LeagueClient.SetupGet(client => client.Token).Returns("test-token");
            context.LeagueClient.SetupGet(client => client.ProcessId).Returns(1234);
            context.LeagueClient.Setup(client => client.StartAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            context.LeagueClient.Setup(client => client.StopAsync(
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            context.LeagueClient.Setup(client => client.Subscribe(
                    It.IsAny<string>(), It.IsAny<Action<OnWebsocketEventArgs>>()))
                .Callback<string, Action<OnWebsocketEventArgs>>((uri, handler) =>
                    context.Subscriptions[uri] = handler);

            context.HttpService.SetupGet(service => service.IsInitialized)
                .Returns(() => context.HttpInitialized);
            context.HttpService.Setup(service => service.Initialize(
                    It.IsAny<int>(), It.IsAny<string>()))
                .Callback(() => context.HttpInitialized = true);
            context.HttpService.Setup(service => service.Reset())
                .Callback(() => context.HttpInitialized = false);

            context.GameService.Setup(service => service.GetGameflowPhaseAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => context.Phase);
            context.GameService.Setup(service => service.GetGameflowSessionSnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => context.GameflowSession);
            context.GameService.Setup(service => service.GetLobbySnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((LobbySnapshot)null);
            context.GameService.Setup(service => service.GetMatchmakingSnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((MatchmakingSnapshot)null);
            context.GameService.Setup(service => service.GetReadyCheckSnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ReadyCheckSnapshot)null);
            context.GameService.Setup(service => service.GetChampionSelectSnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ChampionSelectSnapshot)null);
            context.GameService.Setup(service => service.GetPostGameSnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((PostGameSnapshot)null);
            context.GameService.Setup(service => service.ReconnectGameAsync(
                    It.IsAny<CancellationToken>()))
                .Callback(() => context.ReconnectRequested.TrySetResult(true))
                .Returns(Task.CompletedTask);

            context.AutomationSettings.SetupGet(settings => settings.AutoAcceptReadyCheck)
                .Returns(false);
            context.AutomationSettings.SetupGet(settings => settings.AutoReconnect)
                .Returns(true);

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
            string phase,
            bool running)
        {
            return new GameflowSessionSnapshot
            {
                Phase = phase,
                GameClient = new GameflowClientState
                {
                    Running = running
                }
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

            public TaskCompletionSource<bool> ReconnectRequested { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public MatchService Service { get; set; }

            public GameflowSessionSnapshot GameflowSession { get; set; }

            public string Phase { get; set; }

            public bool HttpInitialized { get; set; }
        }
    }
}
