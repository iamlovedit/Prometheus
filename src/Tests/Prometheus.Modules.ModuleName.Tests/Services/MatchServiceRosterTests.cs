using Moq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Concurrent;
using Xunit;
using MatchModel = Prometheus.Core.Models.Match;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class MatchServiceRosterTests
    {
        [Fact]
        public async Task ChampionSelect_PublishesFivePerSideAndNeverQueriesEnemyIdentity()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect(
                "ally-puuid", "ally-2", "ally-3", "ally-4", "ally-5");
            context.ChampionSelect.TheirTeam =
            [
                new ChampionSelectTeamMemberSnapshot
                {
                    CellId = 10,
                    ChampionId = 99,
                    Puuid = "enemy-secret-puuid",
                    Spell1Id = 4,
                    Spell2Id = 12
                }
            ];
            context.SummonerService.Setup(service => service.GetRankStatsByPuuid(
                    "ally-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    {"queueMap":{"RANKED_SOLO_5x5":{"tier":"GOLD","division":"II","leaguePoints":45}}}
                    """);
            context.SummonerService.Setup(service => service.GetMatchHistoryAsync(
                    "ally-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryQueryResult
                {
                    Succeeded = true,
                    Matches =
                    [
                        CreateMatch(true, 5, 2, 5),
                        CreateMatch(false, 0, 0, 3),
                        CreateMatch(true, 3, 3, 1)
                    ]
                });

            await context.Service.StartAsync();
            var snapshot = await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam.Count == 5 &&
                value.Roster.MyTeam.All(player =>
                    player.DataState == LiveMatchPlayerDataState.Loaded));

            Assert.Equal(5, snapshot.Roster.MyTeam.Count);
            Assert.Equal(5, snapshot.Roster.TheirTeam.Count);
            Assert.Equal(string.Empty,
                snapshot.ChampionSelect.TheirTeam[0].Puuid);
            Assert.All(snapshot.Roster.TheirTeam, player =>
            {
                Assert.True(player.IsHidden);
                Assert.Equal(string.Empty, player.Puuid);
                Assert.Null(player.Summoner);
                Assert.Equal(LiveMatchPlayerDataState.Hidden, player.DataState);
            });

            var ally = snapshot.Roster.MyTeam[0];
            Assert.Equal(Tier.GOLD, ally.SoloRank.Tier);
            Assert.Equal("II", ally.SoloRank.Division);
            Assert.Equal(2, ally.RecentWins);
            Assert.Equal(1, ally.RecentLosses);
            Assert.Equal(3, ally.RecentMatchCount);
            Assert.Equal(3.4, ally.AverageKda, 3);
            Assert.Equal(new[] { true, false, true }, ally.RecentResults);
            Assert.Equal("champion-1", ally.ChampionIcon);
            Assert.Equal("spell-4", ally.Spell1Icon);
            Assert.Equal("spell-12", ally.Spell2Icon);

            context.SummonerService.Verify(service => service.SearchSummonerByPuuid(
                "enemy-secret-puuid", It.IsAny<CancellationToken>()), Times.Never);
            context.SummonerService.Verify(service => service.GetRankStatsByPuuid(
                "enemy-secret-puuid", It.IsAny<CancellationToken>()), Times.Never);
            context.SummonerService.Verify(service => service.GetMatchHistoryAsync(
                "enemy-secret-puuid",
                It.IsAny<CancellationToken>()), Times.Never);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task GameflowRealShape_MergesChampionSelectionSpellsByPuuidForBothTeams()
        {
            var context = CreateContext();
            context.Phase = "InProgress";
            context.GameflowSession = CreateRealShapeGameflowSession("InProgress");

            await context.Service.StartAsync();
            var snapshot = await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.SourcePhase == GameflowPhase.InProgress &&
                value.Roster.MyTeam.Count == 5 &&
                value.Roster.TheirTeam.Count == 5 &&
                value.Roster.MyTeam.Concat(value.Roster.TheirTeam).All(player =>
                    player.DataState == LiveMatchPlayerDataState.Loaded));

            Assert.Contains(snapshot.Roster.MyTeam, player =>
                player.Puuid == "local-puuid" && player.IsLocalPlayer);
            Assert.DoesNotContain(snapshot.Roster.TheirTeam,
                player => player.IsLocalPlayer);

            var local = Assert.Single(snapshot.Roster.MyTeam,
                player => player.Puuid == "local-puuid");
            Assert.Equal(201, local.ChampionId);
            Assert.Equal(4, local.Spell1Id);
            Assert.Equal(12, local.Spell2Id);
            Assert.Equal("spell-4", local.Spell1Icon);
            Assert.Equal("spell-12", local.Spell2Icon);

            var enemy = Assert.Single(snapshot.Roster.TheirTeam,
                player => player.Puuid == "enemy-1");
            Assert.Equal(101, enemy.ChampionId);
            Assert.Equal(32, enemy.Spell1Id);
            Assert.Equal(4, enemy.Spell2Id);
            Assert.Equal("spell-32", enemy.Spell1Icon);
            Assert.Equal("spell-4", enemy.Spell2Icon);
            Assert.False(enemy.IsHidden);

            context.SummonerService.Verify(service => service.SearchSummonerByPuuid(
                "enemy-1", It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetRankStatsByPuuid(
                "enemy-1", It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetMatchHistoryAsync(
                "enemy-1", It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetCurrentSummoner(
                It.IsAny<CancellationToken>()), Times.Once);

            await context.Service.StopAsync();
        }

        [Theory]
        [InlineData("GameStart")]
        [InlineData("InProgress")]
        public async Task ChampionSelectThenGameflow_LoadsAlliesThenRevealsAndLoadsBothTeams(
            string gameflowPhase)
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect(
                "local-puuid", "ally-1", "ally-2", "ally-3", "ally-4");
            context.ChampionSelect.GameId = 500838514588;
            context.ChampionSelect.TheirTeam = Enumerable.Range(1, 5)
                .Select(index => new ChampionSelectTeamMemberSnapshot
                {
                    CellId = 10 + index,
                    ChampionId = 100 + index,
                    Puuid = $"champ-select-enemy-{index}"
                })
                .ToList();
            context.GameflowSession = CreateRealShapeGameflowSession(gameflowPhase);

            await context.Service.StartAsync();
            var championSelect = await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.SourcePhase == GameflowPhase.ChampSelect &&
                value.Roster.MyTeam.Count == 5 &&
                value.Roster.MyTeam.All(player =>
                    player.DataState == LiveMatchPlayerDataState.Loaded));

            Assert.Equal(new[]
            {
                "local-puuid", "ally-1", "ally-2", "ally-3", "ally-4"
            }, championSelect.Roster.MyTeam.Select(player => player.Puuid));
            Assert.All(championSelect.Roster.TheirTeam, player =>
            {
                Assert.True(player.IsHidden);
                Assert.Equal(string.Empty, player.Puuid);
                Assert.Equal(LiveMatchPlayerDataState.Hidden, player.DataState);
            });
            context.SummonerService.Verify(service => service.SearchSummonerByPuuid(
                "enemy-1", It.IsAny<CancellationToken>()), Times.Never);

            var transitionSnapshots = new ConcurrentQueue<LiveMatchSnapshot>();
            EventHandler<LiveMatchSnapshotChangedEventArgs> transitionHandler = (_, args) =>
            {
                if (args.Snapshot.GameflowPhase is GameflowPhase.GameStart or
                    GameflowPhase.InProgress)
                {
                    transitionSnapshots.Enqueue(args.Snapshot);
                }
            };
            context.Service.SnapshotChanged += transitionHandler;
            context.PublishPhase(gameflowPhase);
            var expectedPhase = Enum.Parse<GameflowPhase>(gameflowPhase);
            var inGame = await WaitForSnapshotAsync(context.Service, value =>
                value.GameflowPhase == expectedPhase &&
                value.Roster?.SourcePhase == expectedPhase &&
                value.Roster.MyTeam.Count == 5 &&
                value.Roster.TheirTeam.Count == 5 &&
                value.Roster.MyTeam.Concat(value.Roster.TheirTeam).All(player =>
                    player.DataState == LiveMatchPlayerDataState.Loaded));
            context.Service.SnapshotChanged -= transitionHandler;

            Assert.Contains(inGame.Roster.MyTeam, player =>
                player.Puuid == "local-puuid" && player.IsLocalPlayer);
            Assert.Equal(new[]
            {
                "local-puuid", "ally-1", "ally-2", "ally-3", "ally-4"
            }, inGame.Roster.MyTeam.Select(player => player.Puuid));
            Assert.Equal(new[]
            {
                "enemy-1", "enemy-2", "enemy-3", "enemy-4", "enemy-5"
            }, inGame.Roster.TheirTeam.Select(player => player.Puuid));
            Assert.All(inGame.Roster.MyTeam.Concat(inGame.Roster.TheirTeam), player =>
                Assert.False(player.IsHidden));
            Assert.DoesNotContain(transitionSnapshots, snapshot =>
                snapshot.Roster is null || snapshot.Roster.MyTeam.Count == 0);

            context.SummonerService.Verify(service => service.SearchSummonerByPuuid(
                "enemy-1", It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetRankStatsByPuuid(
                "enemy-1", It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetMatchHistoryAsync(
                "enemy-1", It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetCurrentSummoner(
                It.IsAny<CancellationToken>()), Times.Never);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task ChampionSelect_WhenGameClientStarts_UsesCompleteGameflowRoster()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect(
                "local-puuid", "ally-1", "ally-2", "ally-3", "ally-4");
            context.ChampionSelect.GameId = 500838514588;
            context.GameflowSession = CreateRealShapeGameflowSession("ChampSelect");
            context.GameflowSession.GameClient = new GameflowClientState
            {
                Running = false
            };

            await context.Service.StartAsync();
            var championSelect = await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.SourcePhase == GameflowPhase.ChampSelect &&
                value.Roster.MyTeam.All(player =>
                    player.DataState == LiveMatchPlayerDataState.Loaded));
            Assert.All(championSelect.Roster.TheirTeam, player =>
                Assert.True(player.IsHidden));

            context.GameflowSession.GameClient.Running = true;
            context.PublishSession();
            var inGameRoster = await WaitForSnapshotAsync(context.Service, value =>
                value.GameflowPhase == GameflowPhase.ChampSelect &&
                value.Roster?.MyTeam.Count == 5 &&
                value.Roster.TheirTeam.Count == 5 &&
                value.Roster.TheirTeam.Any(player => player.Puuid == "enemy-1") &&
                value.Roster.MyTeam.Concat(value.Roster.TheirTeam).All(player =>
                    player.DataState == LiveMatchPlayerDataState.Loaded));

            Assert.Contains(inGameRoster.Roster.MyTeam, player =>
                player.Puuid == "local-puuid" && player.IsLocalPlayer);
            Assert.All(inGameRoster.Roster.TheirTeam, player =>
                Assert.False(player.IsHidden));
            context.SummonerService.Verify(service => service.SearchSummonerByPuuid(
                "enemy-1", It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetCurrentSummoner(
                It.IsAny<CancellationToken>()), Times.Never);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task PlayerEnrichment_NeverRunsMoreThanFourPlayersConcurrently()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect(
                "ally-1", "ally-2", "ally-3", "ally-4", "ally-5");
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var fourEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var active = 0;
            var maximum = 0;
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string puuid, CancellationToken cancellationToken) =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximum, current);
                    if (current >= 4)
                    {
                        fourEntered.TrySetResult(true);
                    }

                    try
                    {
                        await release.Task.WaitAsync(cancellationToken);
                        return CreateSummoner(puuid);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                });

            await context.Service.StartAsync();
            await fourEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(4, Volatile.Read(ref maximum));
            release.TrySetResult(true);
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam.Count(player =>
                    player.DataState == LiveMatchPlayerDataState.Loaded) == 5);
            Assert.Equal(4, Volatile.Read(ref maximum));

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task SameRosterUsesSuccessfulCache_ManualRefreshClearsIt()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect("ally-puuid");
            var searches = 0;
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "ally-puuid", It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref searches))
                .ReturnsAsync(CreateSummoner("ally-puuid"));

            await context.Service.StartAsync();
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].DataState == LiveMatchPlayerDataState.Loaded);
            Assert.Equal(1, Volatile.Read(ref searches));

            var version = context.Service.Current.Version;
            context.PublishChampionSelect();
            Assert.Equal(1, Volatile.Read(ref searches));
            Assert.Equal(version + 1, context.Service.Current.Version);

            await context.Service.RefreshAsync();
            await WaitUntilAsync(() => Volatile.Read(ref searches) == 2);
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].DataState == LiveMatchPlayerDataState.Loaded);
            Assert.Equal(2, Volatile.Read(ref searches));

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task MissingGameId_DoesNotReusePlayerDataAcrossMatches()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect("ally-puuid");
            context.ChampionSelect.GameId = 0;
            var searches = 0;
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "ally-puuid", It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref searches))
                .ReturnsAsync(CreateSummoner("ally-puuid"));

            await context.Service.StartAsync();
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].DataState == LiveMatchPlayerDataState.Loaded);
            Assert.Equal(1, Volatile.Read(ref searches));

            context.PublishPhase("None");
            await WaitForSnapshotAsync(context.Service, value =>
                value.GameflowPhase == GameflowPhase.None && value.Roster is null);

            context.ChampionSelect = CreateChampionSelect("ally-puuid");
            context.ChampionSelect.GameId = 0;
            context.PublishPhase("ChampSelect");
            await WaitUntilAsync(() => Volatile.Read(ref searches) == 2);
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].DataState == LiveMatchPlayerDataState.Loaded);

            Assert.Equal(2, Volatile.Read(ref searches));
            await context.Service.StopAsync();
        }

        [Fact]
        public async Task FailedPlayerLoad_IsNotCachedAndSameRosterCanRetry()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect("ally-puuid");
            var attempts = 0;
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "ally-puuid", It.IsAny<CancellationToken>()))
                .Returns((string puuid, CancellationToken _) =>
                {
                    if (Interlocked.Increment(ref attempts) == 1)
                    {
                        return Task.FromException<SummonerAccount>(
                            new InvalidOperationException("temporary failure"));
                    }
                    return Task.FromResult(CreateSummoner(puuid));
                });

            await context.Service.StartAsync();
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].DataState == LiveMatchPlayerDataState.Error);
            await WaitUntilAsync(() => Volatile.Read(ref attempts) == 1);

            context.PublishChampionSelect();
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].DataState == LiveMatchPlayerDataState.Loaded);
            Assert.Equal(2, Volatile.Read(ref attempts));

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task EmptyRankResponse_IsAnErrorInsteadOfUnranked()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect("ally-puuid");
            context.SummonerService.Setup(service => service.GetRankStatsByPuuid(
                    "ally-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync((string)null);

            await context.Service.StartAsync();
            var snapshot = await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].DataState == LiveMatchPlayerDataState.Error);

            Assert.Null(snapshot.Roster.MyTeam[0].SoloRank);
            Assert.NotEmpty(snapshot.Roster.MyTeam[0].Error);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task GameflowCurrentSummonerFailure_AllowsSameRosterEventToRetry()
        {
            var context = CreateContext();
            context.Phase = "InProgress";
            context.GameflowSession = CreateGameflowSession();
            context.ChampionSelect = CreateChampionSelect("stale-bp-player");
            var attempts = 0;
            context.SummonerService.Setup(service => service.GetCurrentSummoner(
                    It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(Interlocked.Increment(ref attempts) == 1
                    ? null
                    : CreateSummoner("local-puuid", 100)));

            await context.Service.StartAsync();
            await WaitUntilAsync(() => Volatile.Read(ref attempts) == 1);
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster is { IsResolving: false } &&
                value.Roster.MyTeam.Count == 0);

            context.PublishSession();
            var snapshot = await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam.Count == 5 &&
                value.Roster.MyTeam.Any(player => player.IsLocalPlayer));

            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.Equal(888, snapshot.Roster.GameId);
            Assert.Contains(snapshot.Roster.MyTeam,
                player => player.Puuid == "local-puuid" && player.IsLocalPlayer);
            Assert.Contains(snapshot.Roster.TheirTeam,
                player => player.Puuid == "enemy-1");

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task LateVisualResultFromOldRoster_DoesNotChangeCurrentOrVersion()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect("ally-a");
            context.ChampionSelect.MyTeam[0].ChampionId = 101;
            var oldVisual = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.GameResourceManager.Setup(manager =>
                    manager.GetChampoinIconByIdAsync(101))
                .Returns(oldVisual.Task);
            context.GameResourceManager.Setup(manager =>
                    manager.GetChampoinIconByIdAsync(102))
                .ReturnsAsync("champion-b");

            await context.Service.StartAsync();
            await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].Puuid == "ally-a" &&
                value.Roster.MyTeam[0].DataState == LiveMatchPlayerDataState.Loaded);

            context.ChampionSelect = CreateChampionSelect("ally-b");
            context.ChampionSelect.MyTeam[0].ChampionId = 102;
            context.PublishChampionSelect();
            var current = await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].Puuid == "ally-b" &&
                value.Roster.MyTeam[0].DataState == LiveMatchPlayerDataState.Loaded &&
                value.Roster.MyTeam[0].ChampionIcon == "champion-b");
            var version = current.Version;

            oldVisual.TrySetResult("stale-champion-a");
            await Task.Delay(100);

            Assert.Equal(version, context.Service.Current.Version);
            Assert.Equal("ally-b", context.Service.Current.Roster.MyTeam[0].Puuid);
            Assert.Equal("champion-b",
                context.Service.Current.Roster.MyTeam[0].ChampionIcon);

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task ConcurrentResourceEvents_SerializeSnapshotNotificationsByVersion()
        {
            var context = CreateContext();
            context.Phase = "None";
            await context.Service.StartAsync();

            var versions = new ConcurrentQueue<long>();
            var activeObservers = 0;
            var maximumObservers = 0;
            EventHandler<LiveMatchSnapshotChangedEventArgs> handler = (_, args) =>
            {
                var active = Interlocked.Increment(ref activeObservers);
                UpdateMaximum(ref maximumObservers, active);
                try
                {
                    Thread.Sleep(2);
                    versions.Enqueue(args.Snapshot.Version);
                }
                finally
                {
                    Interlocked.Decrement(ref activeObservers);
                }
            };
            context.Service.SnapshotChanged += handler;
            try
            {
                var publish = context.Subscriptions["/lol-lobby/v2/lobby"];
                await Task.WhenAll(Enumerable.Range(0, 32).Select(index =>
                    Task.Run(() => publish(new OnWebsocketEventArgs
                    {
                        Data = new LobbySnapshot { PartyId = index.ToString() },
                        EventType = "Update",
                        Uri = "/lol-lobby/v2/lobby"
                    }))));
            }
            finally
            {
                context.Service.SnapshotChanged -= handler;
            }

            var publishedVersions = versions.ToArray();
            Assert.Equal(32, publishedVersions.Length);
            Assert.Equal(1, Volatile.Read(ref maximumObservers));
            Assert.All(publishedVersions.Zip(publishedVersions.Skip(1)), pair =>
                Assert.True(pair.Second > pair.First));

            await context.Service.StopAsync();
        }

        [Fact]
        public async Task PublishedSnapshots_CannotMutateServiceState()
        {
            var context = CreateContext();
            context.Phase = "ChampSelect";
            context.ChampionSelect = CreateChampionSelect("ally-puuid");
            await context.Service.StartAsync();
            var loaded = await WaitForSnapshotAsync(context.Service, value =>
                value.Roster?.MyTeam[0].DataState == LiveMatchPlayerDataState.Loaded);

            var exposed = context.Service.Current;
            exposed.GameflowPhase = GameflowPhase.None;
            exposed.ChampionSelect.MyTeam[0].Puuid = "tampered-raw";
            exposed.Roster.MyTeam[0].Summoner.GameName = "tampered-player";

            var fresh = context.Service.Current;
            Assert.Equal(GameflowPhase.ChampSelect, fresh.GameflowPhase);
            Assert.Equal("ally-puuid", fresh.ChampionSelect.MyTeam[0].Puuid);
            Assert.Equal("ally-puuid", fresh.Roster.MyTeam[0].Summoner.GameName);

            EventHandler<LiveMatchSnapshotChangedEventArgs> mutatingHandler = (_, args) =>
            {
                args.Snapshot.GameflowPhase = GameflowPhase.None;
                args.Snapshot.Roster.MyTeam[0].Summoner.GameName = "event-tampered";
            };
            context.Service.SnapshotChanged += mutatingHandler;
            try
            {
                context.Subscriptions["/lol-lobby/v2/lobby"](
                    new OnWebsocketEventArgs
                    {
                        Data = new LobbySnapshot { PartyId = "immutability-check" },
                        EventType = "Update",
                        Uri = "/lol-lobby/v2/lobby"
                    });
            }
            finally
            {
                context.Service.SnapshotChanged -= mutatingHandler;
            }

            var afterEvent = context.Service.Current;
            Assert.True(afterEvent.Version > loaded.Version);
            Assert.Equal(GameflowPhase.ChampSelect, afterEvent.GameflowPhase);
            Assert.Equal("ally-puuid",
                afterEvent.Roster.MyTeam[0].Summoner.GameName);

            await context.Service.StopAsync();
        }

        private static TestContext CreateContext()
        {
            var context = new TestContext();
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
            context.LeagueClient.Setup(client => client.Subscribe(It.IsAny<string>(),
                    It.IsAny<Action<OnWebsocketEventArgs>>()))
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
                .Returns(() => Task.FromResult(context.Phase));
            context.GameService.Setup(service => service.GetGameflowSessionSnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(context.GameflowSession));
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
                .Returns(() => Task.FromResult(context.ChampionSelect));
            context.GameService.Setup(service => service.GetPostGameSnapshotAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((PostGameSnapshot)null);

            context.SummonerService.Setup(service => service.GetCurrentSummoner(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSummoner("local-puuid", 100));
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string puuid, CancellationToken _) => CreateSummoner(puuid));
            context.SummonerService.Setup(service => service.GetRankStatsByPuuid(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("{\"queueMap\":{}}");
            context.SummonerService.Setup(service => service.GetMatchHistoryAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryQueryResult
                {
                    Succeeded = true,
                    Matches = []
                });

            context.GameResourceManager.Setup(manager =>
                    manager.GetChampoinIconByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => $"champion-{id}");
            context.GameResourceManager.Setup(manager =>
                    manager.GetSpellIconByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => $"spell-{id}");

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

        private static ChampionSelectSnapshot CreateChampionSelect(params string[] allies)
        {
            return new ChampionSelectSnapshot
            {
                GameId = 777,
                LocalPlayerCellId = 1,
                MyTeam = allies.Select((puuid, index) =>
                    new ChampionSelectTeamMemberSnapshot
                    {
                        CellId = index + 1,
                        ChampionId = index + 1,
                        Spell1Id = 4,
                        Spell2Id = 12,
                        Puuid = puuid,
                        AssignedPosition = index switch
                        {
                            0 => "TOP",
                            1 => "JUNGLE",
                            2 => "MIDDLE",
                            3 => "BOTTOM",
                            _ => "UTILITY"
                        }
                    }).ToList(),
                TheirTeam = []
            };
        }

        private static GameflowSessionSnapshot CreateGameflowSession()
        {
            return new GameflowSessionSnapshot
            {
                Phase = "InProgress",
                GameData = new GameflowGameData
                {
                    GameId = 888,
                    TeamOne = Enumerable.Range(0, 5).Select(index =>
                        new GameflowTeamMember
                        {
                            CellId = index + 1,
                            ChampionId = index + 1,
                            Puuid = index == 0 ? "local-puuid" : $"ally-{index}",
                            SummonerId = index == 0 ? 100 : 100 + index,
                            SummonerName = index == 0 ? "Local" : $"Ally {index}",
                            TeamId = 100
                        }).ToList(),
                    TeamTwo = Enumerable.Range(1, 5).Select(index =>
                        new GameflowTeamMember
                        {
                            CellId = 10 + index,
                            ChampionId = 10 + index,
                            Puuid = $"enemy-{index}",
                            SummonerId = 200 + index,
                            SummonerName = $"Enemy {index}",
                            TeamId = 200
                        }).ToList()
                }
            };
        }

        private static GameflowSessionSnapshot CreateRealShapeGameflowSession(string phase)
        {
            var teamOne = Enumerable.Range(1, 5).Select(index =>
                new GameflowTeamMember
                {
                    ChampionId = 100 + index,
                    ProfileIconId = 2000 + index,
                    Puuid = $"enemy-{index}",
                    SelectedPosition = "NONE",
                    SummonerId = 200 + index,
                    TeamParticipantId = 5 + index
                }).ToList();
            var teamTwo = Enumerable.Range(0, 5).Select(index =>
                new GameflowTeamMember
                {
                    ChampionId = 201 + index,
                    ProfileIconId = 3000 + index,
                    Puuid = index == 0 ? "local-puuid" : $"ally-{index}",
                    SelectedPosition = "NONE",
                    SummonerId = index == 0 ? 100 : 100 + index,
                    TeamParticipantId = index + 1
                }).ToList();
            var selections = teamTwo.AsEnumerable().Reverse()
                .Concat(teamOne.AsEnumerable().Reverse())
                .Select(member => new GameflowPlayerSelection
                {
                    ChampionId = member.ChampionId,
                    Puuid = member.Puuid,
                    SelectedSkinIndex = member.ChampionId % 7,
                    Spell1Id = member.Puuid == "enemy-1" ? 32 : 4,
                    Spell2Id = member.Puuid == "local-puuid" ? 12 : 4
                })
                .ToList();

            return new GameflowSessionSnapshot
            {
                Phase = phase,
                GameData = new GameflowGameData
                {
                    GameId = 500838514588,
                    PlayerChampionSelections = selections,
                    TeamOne = teamOne,
                    TeamTwo = teamTwo
                }
            };
        }

        private static SummonerAccount CreateSummoner(string puuid, long summonerId = 0)
        {
            return new SummonerAccount
            {
                Puuid = puuid,
                SummonerId = summonerId,
                GameName = puuid,
                TagLine = "CN1"
            };
        }

        private static MatchModel CreateMatch(bool win, int kills, int deaths, int assists)
        {
            return new MatchModel
            {
                Participants =
                [
                    new Participant
                    {
                        Stats = new MatchStats
                        {
                            Win = win,
                            Kills = kills,
                            Deaths = deaths,
                            Assists = assists
                        }
                    }
                ]
            };
        }

        private static async Task<LiveMatchSnapshot> WaitForSnapshotAsync(
            MatchService service, Func<LiveMatchSnapshot, bool> predicate)
        {
            var current = service.Current;
            if (predicate(current))
            {
                return current;
            }

            var completion = new TaskCompletionSource<LiveMatchSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<LiveMatchSnapshotChangedEventArgs> handler = null;
            handler = (_, args) =>
            {
                if (predicate(args.Snapshot))
                {
                    completion.TrySetResult(args.Snapshot);
                }
            };
            service.SnapshotChanged += handler;
            try
            {
                current = service.Current;
                if (predicate(current))
                {
                    return current;
                }
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                service.SnapshotChanged -= handler;
            }
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!predicate())
            {
                await Task.Delay(10, cts.Token);
            }
        }

        private static void UpdateMaximum(ref int maximum, int value)
        {
            var observed = Volatile.Read(ref maximum);
            while (value > observed)
            {
                var previous = Interlocked.CompareExchange(ref maximum, value, observed);
                if (previous == observed)
                {
                    return;
                }
                observed = previous;
            }
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

            public string Phase { get; set; } = "None";

            public ChampionSelectSnapshot ChampionSelect { get; set; }

            public GameflowSessionSnapshot GameflowSession { get; set; }

            public bool HttpInitialized { get; set; }

            public void PublishChampionSelect()
            {
                Subscriptions["/lol-champ-select/v1/session"](
                    new OnWebsocketEventArgs
                    {
                        Data = ChampionSelect,
                        EventType = "Update",
                        Uri = "/lol-champ-select/v1/session"
                    });
            }

            public void PublishSession()
            {
                Subscriptions["/lol-gameflow/v1/session"](
                    new OnWebsocketEventArgs
                    {
                        Data = GameflowSession,
                        EventType = "Update",
                        Uri = "/lol-gameflow/v1/session"
                    });
            }

            public void PublishPhase(string phase)
            {
                Phase = phase;
                Subscriptions["/lol-gameflow/v1/gameflow-phase"](
                    new OnWebsocketEventArgs
                    {
                        Data = phase,
                        EventType = "Update",
                        Uri = "/lol-gameflow/v1/gameflow-phase"
                    });
            }
        }
    }
}
