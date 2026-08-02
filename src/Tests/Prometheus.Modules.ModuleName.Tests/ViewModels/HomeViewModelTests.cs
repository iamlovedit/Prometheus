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
        public void DisconnectedSnapshot_ShowsLaunchGameAction()
        {
            using var context = new TestContext();

            Assert.Equal("HomePage.Action.LaunchGame",
                context.ViewModel.PrimaryActionText);
            Assert.True(context.ViewModel.CanPrimaryAction);
            Assert.True(context.ViewModel.PrimaryActionCommand.CanExecute());
        }

        [Fact]
        public async Task LaunchGameAction_DisablesUntilClientConnects()
        {
            using var context = new TestContext();
            var requested = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.LeagueClientLauncher.Setup(launcher => launcher.LaunchAsync(
                    It.IsAny<CancellationToken>()))
                .Callback(() => requested.TrySetResult(true))
                .ReturnsAsync(LeagueClientLaunchStatus.Started);

            context.ViewModel.PrimaryActionCommand.Execute();

            await requested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(context.ViewModel.IsLaunchingGame);
            Assert.False(context.ViewModel.CanPrimaryAction);
            Assert.Equal("HomePage.Action.LaunchingGame",
                context.ViewModel.PrimaryActionText);

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.False(context.ViewModel.IsLaunchingGame);
            Assert.Equal("HomePage.ViewCareer", context.ViewModel.PrimaryActionText);
        }

        [Fact]
        public void LaunchGameAction_WhenLauncherIsMissing_ShowsLocalizedError()
        {
            using var context = new TestContext();
            context.LeagueClientLauncher.Setup(launcher => launcher.LaunchAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(LeagueClientLaunchStatus.LauncherNotFound);

            context.ViewModel.PrimaryActionCommand.Execute();

            Assert.False(context.ViewModel.IsLaunchingGame);
            Assert.True(context.ViewModel.CanPrimaryAction);
            Assert.Equal("HomePage.Launch.NotFound", context.ViewModel.ErrorText);
        }

        [Fact]
        public void LaunchGameAction_WhenExternalLauncherIsRequired_ShowsLocalizedError()
        {
            using var context = new TestContext();
            context.LeagueClientLauncher.Setup(launcher => launcher.LaunchAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(LeagueClientLaunchStatus.ExternalLauncherRequired);

            context.ViewModel.PrimaryActionCommand.Execute();

            Assert.False(context.ViewModel.IsLaunchingGame);
            Assert.True(context.ViewModel.CanPrimaryAction);
            Assert.Equal("HomePage.Launch.ExternalLauncherRequired",
                context.ViewModel.ErrorText);
        }

        [Fact]
        public void ReconnectingSnapshot_WithLeagueProcessRunning_DoesNotOfferLaunch()
        {
            using var context = new TestContext(leagueClientRunning: true);

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Reconnecting,
                GameflowPhase = GameflowPhase.Unknown,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.Equal("HomePage.Action.Syncing",
                context.ViewModel.PrimaryActionText);
            Assert.False(context.ViewModel.CanPrimaryAction);
        }

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
            context.SummonerService.Verify(service => service.GetMatchHistoryAsync(
                "test-puuid",
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal("Prometheus", context.ViewModel.SummonerName);
            Assert.Equal("#TST", context.ViewModel.SummonerTag);
            Assert.Equal("100", context.ViewModel.SummonerLevel);
            Assert.Equal(42, context.ViewModel.PercentCompleteForNextLevel);
            Assert.Equal(840, context.ViewModel.XpSinceLastLevel);
            Assert.Equal(1160, context.ViewModel.XpUntilNextLevel);
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
        public void ConnectedIdleSnapshot_RequestsFiveMatchesForHome()
        {
            var matches = Enumerable.Range(1, 5)
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

            context.SummonerService.Verify(service => service.GetMatchHistoryAsync(
                "test-puuid",
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
        public void AutoSwapAramBench_FromHome_UpdatesSharedSettingAndStatus()
        {
            using var context = new TestContext();

            context.ViewModel.AutoSwapAramBench = true;

            Assert.True(context.AutomationSettings.Object.AutoSwapAramBench);
            Assert.Equal("HomePage.Automation.AramSwapOn",
                context.ViewModel.AutomationStatus);
        }

        [Fact]
        public void AutoSwapAramBench_WhenSettingsChange_RefreshesHomeToggle()
        {
            using var context = new TestContext();
            var changedProperties = new List<string>();
            context.ViewModel.PropertyChanged += (_, args) =>
                changedProperties.Add(args.PropertyName);

            context.SetAutoSwapAramBench(true);

            Assert.True(context.ViewModel.AutoSwapAramBench);
            Assert.Contains(nameof(HomeViewModel.AutoSwapAramBench),
                changedProperties);
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

        [Fact]
        public void QuickMatchCommands_AreEnabledWhenConnectedAndConfigurable()
        {
            using var context = new TestContext();
            var command = context.ViewModel.QuickStartSoloDuoCommand;

            Assert.False(command.CanExecute());

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.True(command.CanExecute());

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.Lobby,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.True(command.CanExecute());

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.Matchmaking,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.False(command.CanExecute());
        }

        [Theory]
        [InlineData(GameQueueIds.RankedSoloDuo)]
        [InlineData(GameQueueIds.RankedFlex)]
        [InlineData(GameQueueIds.Aram)]
        [InlineData(GameQueueIds.HextechAram)]
        public async Task QuickMatchCommand_UsesExpectedQueueId(int queueId)
        {
            using var context = new TestContext();
            var invoked = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.GameService.Setup(service => service.CreateMatchmadeLobbyAsync(
                    queueId,
                    It.IsAny<CancellationToken>()))
                .Callback<int, CancellationToken>((value, _) =>
                    invoked.TrySetResult(value))
                .ReturnsAsync(new MatchmadeLobbyCreationResult
                {
                    Status = MatchmadeLobbyCreationStatus.Created,
                    QueueId = queueId,
                    Lobby = new LobbySnapshot
                    {
                        GameConfig = new LobbyGameConfiguration
                        {
                            QueueId = queueId
                        }
                    }
                });
            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            var command = queueId switch
            {
                GameQueueIds.RankedSoloDuo => context.ViewModel.QuickStartSoloDuoCommand,
                GameQueueIds.RankedFlex => context.ViewModel.QuickStartFlexCommand,
                GameQueueIds.Aram => context.ViewModel.QuickStartAramCommand,
                GameQueueIds.HextechAram => context.ViewModel.QuickStartHextechAramCommand,
                _ => throw new ArgumentOutOfRangeException(nameof(queueId))
            };
            command.Execute();

            Assert.Equal(queueId,
                await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            context.QuickMatchSettings.Verify(settings =>
                settings.SaveQueueId(queueId), Times.Once);
        }

        [Fact]
        public async Task QuickMatchCommand_WhenAlreadyInLobby_ChangesQueue()
        {
            using var context = new TestContext();
            var invoked = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.GameService.Setup(service => service.CreateMatchmadeLobbyAsync(
                    GameQueueIds.RankedFlex,
                    It.IsAny<CancellationToken>()))
                .Callback<int, CancellationToken>((queueId, _) =>
                    invoked.TrySetResult(queueId))
                .ReturnsAsync(new MatchmadeLobbyCreationResult
                {
                    Status = MatchmadeLobbyCreationStatus.Created,
                    QueueId = GameQueueIds.RankedFlex
                });
            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.Lobby,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            context.ViewModel.QuickStartFlexCommand.Execute();

            Assert.Equal(GameQueueIds.RankedFlex,
                await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            context.QuickMatchSettings.Verify(settings =>
                settings.SaveQueueId(GameQueueIds.RankedFlex), Times.Once);
        }

        [Fact]
        public async Task QuickStartSelectedCommand_UsesPersistedQueueId()
        {
            using var context = new TestContext(
                quickMatchQueueId: GameQueueIds.Aram);
            var invoked = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.GameService.Setup(service => service.CreateMatchmadeLobbyAsync(
                    GameQueueIds.Aram,
                    It.IsAny<CancellationToken>()))
                .Callback<int, CancellationToken>((value, _) =>
                    invoked.TrySetResult(value))
                .ReturnsAsync(new MatchmadeLobbyCreationResult
                {
                    Status = MatchmadeLobbyCreationStatus.Created,
                    QueueId = GameQueueIds.Aram
                });
            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            context.ViewModel.QuickStartSelectedCommand.Execute();

            Assert.Equal(GameQueueIds.Aram,
                await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            context.QuickMatchSettings.Verify(settings =>
                settings.SaveQueueId(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void UnsupportedPersistedQueue_DefaultsHomeButtonToSoloDuo()
        {
            using var context = new TestContext(quickMatchQueueId: 9999);

            Assert.Equal("Quick start · Solo/Duo",
                context.ViewModel.QuickStartButtonText);
        }

        [Fact]
        public void QuickMatchSettingsChanged_RefreshesHomeButtonText()
        {
            using var context = new TestContext();
            context.QuickMatchSettings.SetupGet(settings => settings.QueueId)
                .Returns(GameQueueIds.HextechAram);

            context.QuickMatchSettings.Raise(
                settings => settings.Changed += null,
                EventArgs.Empty);

            Assert.Equal("Quick start · ARAM Mayhem",
                context.ViewModel.QuickStartButtonText);
        }

        private sealed class TestContext : IDisposable
        {
            private LiveMatchSnapshot _current = LiveMatchSnapshot.Empty;
            private int[] _preferredAramChampionIds;

            public TestContext(
                int[] preferredAramChampionIds = null,
                IReadOnlyList<MatchModel> recentMatches = null,
                int quickMatchQueueId = GameQueueIds.RankedSoloDuo,
                bool leagueClientRunning = false)
            {
                _preferredAramChampionIds = preferredAramChampionIds ?? [];
                MatchService.SetupGet(service => service.Current)
                    .Returns(() => _current);
                MatchService.SetupGet(service => service.AutomationSettings)
                    .Returns(AutomationSettings.Object);
                AutomationSettings.SetupGet(settings => settings.PreferredAramChampionIds)
                    .Returns(() => _preferredAramChampionIds);
                AutomationSettings.SetupProperty(settings => settings.AutoSwapAramBench);
                AutomationSettings.SetupGet(settings => settings.LastPersistenceSucceeded)
                    .Returns(true);
                ResourceService.Setup(service => service.FindResource<string>(
                        It.IsAny<string>()))
                    .Returns((string key) => key switch
                    {
                        "HomePage.NoSummoner" => "Waiting for summoner data",
                        "HomePage.QuickMatch.Button" => "Quick start · {0}",
                        "HomePage.QuickMatch.SoloDuo" => "Solo/Duo",
                        "HomePage.QuickMatch.Flex" => "Ranked Flex",
                        "HomePage.QuickMatch.Aram" => "ARAM",
                        "HomePage.QuickMatch.HextechAram" => "ARAM Mayhem",
                        _ => key
                    });
                SummonerService.Setup(service => service.GetCurrentSummoner(
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SummonerAccount
                    {
                        GameName = "Prometheus",
                        TagLine = "TST",
                        Puuid = "test-puuid",
                        ProfileIconId = 29,
                        SummonerLevel = 100,
                        PercentCompleteForNextLevel = 42,
                        XpSinceLastLevel = 840,
                        XpUntilNextLevel = 1160
                    });
                SummonerService.Setup(service => service.GetRankStatsByPuuid(
                        "test-puuid", It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string)null);
                SummonerService.Setup(service => service.GetMatchHistoryAsync(
                        "test-puuid", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new MatchHistoryQueryResult
                    {
                        Succeeded = true,
                        Matches = (recentMatches ?? []).ToList()
                    });
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
                GameService.Setup(service => service.CreateMatchmadeLobbyAsync(
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((int queueId, CancellationToken _) =>
                        new MatchmadeLobbyCreationResult
                        {
                            Status = MatchmadeLobbyCreationStatus.Created,
                            QueueId = queueId,
                            Lobby = new LobbySnapshot
                            {
                                GameConfig = new LobbyGameConfiguration
                                {
                                    QueueId = queueId
                                }
                            }
                        });
                QuickMatchSettings.SetupGet(settings => settings.QueueId)
                    .Returns(quickMatchQueueId);
                QuickMatchSettings.Setup(settings => settings.SaveQueueId(
                        It.IsAny<int>()))
                    .Returns(true);
                LeagueClientLauncher.Setup(launcher => launcher.IsLeagueClientRunning())
                    .Returns(leagueClientRunning);
                LeagueClientLauncher.Setup(launcher => launcher.LaunchAsync(
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(LeagueClientLaunchStatus.Started);

                ViewModel = new HomeViewModel(
                    RegionManager.Object,
                    EventAggregator,
                    MatchService.Object,
                    SummonerService.Object,
                    GameResourceManager.Object,
                    ResourceService.Object,
                    ClientService.Object,
                    LeagueClientLauncher.Object,
                    GameService.Object,
                    QuickMatchSettings.Object);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public EventAggregator EventAggregator { get; } = new();

            public Mock<IMatchService> MatchService { get; } = new();

            public Mock<IGameAutomationSettings> AutomationSettings { get; } = new();

            public Mock<ISummonerService> SummonerService { get; } = new();

            public Mock<IGameResourceManager> GameResourceManager { get; } = new();

            public Mock<IResourceService> ResourceService { get; } = new();

            public Mock<IClientService> ClientService { get; } = new();

            public Mock<ILeagueClientLauncher> LeagueClientLauncher { get; } = new();

            public Mock<IGameService> GameService { get; } = new();

            public Mock<IQuickMatchSettings> QuickMatchSettings { get; } = new();

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

            public void SetAutoSwapAramBench(bool value)
            {
                AutomationSettings.Object.AutoSwapAramBench = value;
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
