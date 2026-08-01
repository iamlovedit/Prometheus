using Moq;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Modules.Home.ViewModels;
using Prometheus.Services.Interfaces.Client;
using Xunit;
using MatchModel = Prometheus.Core.Models.Match;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class HomeViewModelTests
    {
        [Fact]
        public void ConnectedSnapshot_AfterOfflinePlaceholder_LoadsCurrentSummoner()
        {
            using var context = new TestContext();

            Assert.Equal("Waiting for summoner data", context.ViewModel.SummonerName);

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            context.SummonerService.Verify(service => service.GetCurrentSummoner(
                It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetMatchesAsync(
                "test-puuid", 0, 19,
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal("Prometheus", context.ViewModel.SummonerName);
            Assert.Equal("#TST", context.ViewModel.SummonerTag);
            Assert.Equal("100", context.ViewModel.SummonerLevel);
            Assert.True(context.ViewModel.ShowSummaryCard);
            Assert.Equal("HomePage.Summary.Recent", context.ViewModel.SummaryTitle);
            Assert.True(context.ViewModel.ShowEmptySummary);
            Assert.Equal("HomePage.Summary.NoMatches", context.ViewModel.EmptySummaryText);
        }

        [Fact]
        public void ConnectedIdleSnapshot_LoadsRecentMatchesForContextCard()
        {
            using var context = new TestContext(recentMatches:
            [
                new MatchModel
                {
                    GameId = 1001,
                    GameCreation = 1_753_958_400_000,
                    GameMode = "ARAM",
                    Participants =
                    [
                        new Participant
                        {
                            ChampionId = 103,
                            Stats = new MatchStats
                            {
                                Win = true,
                                Kills = 8,
                                Deaths = 3,
                                Assists = 7
                            }
                        }
                    ]
                },
                new MatchModel
                {
                    GameId = 1002,
                    GameCreation = 1_753_954_800_000,
                    GameMode = "CLASSIC",
                    Participants =
                    [
                        new Participant
                        {
                            ChampionId = 22,
                            Stats = new MatchStats
                            {
                                Win = false,
                                Kills = 5,
                                Deaths = 6,
                                Assists = 9
                            }
                        }
                    ]
                }
            ]);

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.Collection(
                context.ViewModel.RecentMatches,
                match =>
                {
                    Assert.Equal(1001, match.GameId);
                    Assert.Equal("Ahri", match.ChampionName);
                    Assert.Equal("103.png", match.ChampionIcon);
                    Assert.Equal("ARAM", match.GameMode);
                    Assert.Equal("8/3/7", match.Kda);
                    Assert.True(match.IsWin);
                },
                match =>
                {
                    Assert.Equal(1002, match.GameId);
                    Assert.Equal("Ashe", match.ChampionName);
                    Assert.Equal("22.png", match.ChampionIcon);
                    Assert.Equal("CLASSIC", match.GameMode);
                    Assert.Equal("5/6/9", match.Kda);
                    Assert.False(match.IsWin);
                });
            Assert.True(context.ViewModel.ShowRecentMatches);
            Assert.False(context.ViewModel.ShowEmptySummary);
        }

        [Fact]
        public void ConnectedIdleSnapshot_RequestsTwentyMatchesButDisplaysOnlyFive()
        {
            var matches = Enumerable.Range(1, 20)
                .Select(index => new MatchModel
                {
                    GameId = index,
                    GameCreation = 1_753_958_400_000 - index * 60_000,
                    GameMode = "ARAM",
                    Participants =
                    [
                        new Participant
                        {
                            ChampionId = 103,
                            Stats = new MatchStats
                            {
                                Win = index % 2 == 0,
                                Kills = index,
                                Deaths = 1,
                                Assists = 2
                            }
                        }
                    ]
                })
                .ToArray();
            using var context = new TestContext(recentMatches: matches);

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            context.SummonerService.Verify(service => service.GetMatchesAsync(
                "test-puuid", 0, 19,
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(5, context.ViewModel.RecentMatches.Count);
            Assert.Equal([1L, 2L, 3L, 4L, 5L],
                context.ViewModel.RecentMatches.Select(match => match.GameId));
        }

        [Fact]
        public void OpenSummaryCommand_InIdle_NavigatesToCareer()
        {
            using var context = new TestContext();
            MenuName? destination = null;
            context.EventAggregator.GetEvent<NavigateMenuEvent>().Subscribe(
                menuName => destination = menuName);
            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            context.ViewModel.OpenSummaryCommand.Execute();

            Assert.Equal(MenuName.Career, destination);
        }

        [Fact]
        public void PreferredAramChampions_LoadConfiguredOrderAndIcons()
        {
            using var context = new TestContext([103, 22]);

            Assert.Collection(
                context.ViewModel.PreferredAramChampions,
                champion =>
                {
                    Assert.Equal(1, champion.Priority);
                    Assert.Equal(103, champion.ChampionId);
                    Assert.Equal("Ahri", champion.Name);
                    Assert.Equal("103.png", champion.IconUri);
                },
                champion =>
                {
                    Assert.Equal(2, champion.Priority);
                    Assert.Equal(22, champion.ChampionId);
                    Assert.Equal("Ashe", champion.Name);
                    Assert.Equal("22.png", champion.IconUri);
                });
            Assert.True(context.ViewModel.HasPreferredAramChampions);
            Assert.False(context.ViewModel.ShowPreferredAramChampionEmpty);
        }

        [Fact]
        public void PreferredAramChampions_WhenSettingsChange_RefreshesOverview()
        {
            using var context = new TestContext([103]);

            context.SetPreferredAramChampionIds(22, 103);

            Assert.Collection(
                context.ViewModel.PreferredAramChampions,
                champion =>
                {
                    Assert.Equal(1, champion.Priority);
                    Assert.Equal(22, champion.ChampionId);
                },
                champion =>
                {
                    Assert.Equal(2, champion.Priority);
                    Assert.Equal(103, champion.ChampionId);
                });
        }

        [Fact]
        public void OpenUtilityCommand_NavigatesToUtilitySettings()
        {
            using var context = new TestContext();
            MenuName? destination = null;
            context.EventAggregator.GetEvent<NavigateMenuEvent>().Subscribe(
                menuName => destination = menuName);

            context.ViewModel.OpenUtilityCommand.Execute();

            Assert.Equal(MenuName.Utility, destination);
        }

        private sealed class TestContext : IDisposable
        {
            private LiveMatchSnapshot _current = LiveMatchSnapshot.Empty;
            private int[] _preferredAramChampionIds;

            public TestContext(
                int[] preferredAramChampionIds = null,
                IReadOnlyList<MatchModel> recentMatches = null)
            {
                _preferredAramChampionIds = preferredAramChampionIds ?? [];
                MatchService.SetupGet(service => service.Current)
                    .Returns(() => _current);
                MatchService.SetupGet(service => service.AutomationSettings)
                    .Returns(AutomationSettings.Object);
                AutomationSettings.SetupGet(settings => settings.PreferredAramChampionIds)
                    .Returns(() => _preferredAramChampionIds);
                ResourceService.Setup(service => service.FindResource<string>(
                        It.IsAny<string>()))
                    .Returns((string key) => key == "HomePage.NoSummoner"
                        ? "Waiting for summoner data"
                        : key);
                SummonerService.Setup(service => service.GetCurrentSummoner(
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SummonerAccount
                    {
                        GameName = "Prometheus",
                        TagLine = "TST",
                        Puuid = "test-puuid",
                        ProfileIconId = 29,
                        SummonerLevel = 100
                    });
                SummonerService.Setup(service => service.GetRankStatsByPuuid(
                        "test-puuid", It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string)null);
                SummonerService.Setup(service => service.GetMatchesAsync(
                        "test-puuid", 0, 19, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((recentMatches ?? []).ToList());
                GameResourceManager.Setup(service => service.GetProfileIconByIdAsync(29))
                    .ReturnsAsync("profile-icon.png");
                GameResourceManager.Setup(service => service.GetBackgroundSkinId())
                    .ReturnsAsync((string)null);
                GameResourceManager.Setup(service => service.GetChampionSummarysAsync())
                    .ReturnsAsync(
                    [
                        new ChampionSummary { Id = 103, Name = "Ahri" },
                        new ChampionSummary { Id = 22, Name = "Ashe" }
                    ]);
                GameResourceManager.Setup(service => service.GetChampoinIconByIdAsync(
                        It.IsAny<int>()))
                    .ReturnsAsync((int championId) => $"{championId}.png");

                ViewModel = new HomeViewModel(
                    RegionManager.Object,
                    EventAggregator,
                    MatchService.Object,
                    SummonerService.Object,
                    GameResourceManager.Object,
                    ResourceService.Object,
                    ClientService.Object);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public EventAggregator EventAggregator { get; } = new();

            public Mock<IMatchService> MatchService { get; } = new();

            public Mock<IGameAutomationSettings> AutomationSettings { get; } = new();

            public Mock<ISummonerService> SummonerService { get; } = new();

            public Mock<IGameResourceManager> GameResourceManager { get; } = new();

            public Mock<IResourceService> ResourceService { get; } = new();

            public Mock<IClientService> ClientService { get; } = new();

            public HomeViewModel ViewModel { get; }

            public void Publish(LiveMatchSnapshot snapshot)
            {
                _current = snapshot;
                MatchService.Raise(service => service.SnapshotChanged += null,
                    new LiveMatchSnapshotChangedEventArgs(snapshot));
            }

            public void SetPreferredAramChampionIds(params int[] championIds)
            {
                _preferredAramChampionIds = championIds ?? [];
                AutomationSettings.Raise(
                    settings => settings.Changed += null,
                    EventArgs.Empty);
            }

            public void Dispose()
            {
                ViewModel.Destroy();
            }
        }
    }
}
