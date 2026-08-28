using Moq;
using Prism.Events;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.ViewModels;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class LcuCompanionViewModelTests
    {
        [Fact]
        public void Start_ProjectsFourTeammatesAndExcludesLocalPlayer()
        {
            var snapshot = new LiveMatchSnapshot
            {
                GameflowPhase = GameflowPhase.ChampSelect,
                GameflowSession = new GameflowSessionSnapshot
                {
                    GameData = new GameflowGameData
                    {
                        QueueId = GameQueueIds.RankedSoloDuo
                    }
                },
                ChampionSelect = new ChampionSelectSnapshot
                {
                    LocalPlayerCellId = 1
                },
                Roster = new LiveMatchRosterSnapshot
                {
                    MyTeam = Enumerable.Range(1, 5)
                        .Select(index => new LiveMatchPlayerSnapshot
                        {
                            CellId = index,
                            IsLocalPlayer = index == 1,
                            DisplayName = index == 1 ? "Local" : $"Teammate {index}",
                            DataState = LiveMatchPlayerDataState.Loaded,
                            RecentMatchCount = 20,
                            RecentWins = 10,
                            RecentLosses = 10,
                            RecentResults = Enumerable.Range(0, 25)
                                .Select(match => match % 2 == 0)
                                .ToArray()
                        })
                        .ToArray()
                }
            };
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var automationSettings = new Mock<IGameAutomationSettings>();
            automationSettings.SetupGet(settings => settings.PreferredPickChampionIds)
                .Returns([]);
            automationSettings.SetupGet(settings => settings.PreferredBanChampionIds)
                .Returns([]);
            automationSettings.SetupGet(settings => settings.PreferredAramChampionIds)
                .Returns([]);
            var resourceService = new Mock<IResourceService>();
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string _) => null);
            var viewModel = new LcuCompanionViewModel(
                new EventAggregator(),
                matchService.Object,
                new Mock<IGameService>().Object,
                automationSettings.Object,
                new Mock<IGameResourceManager>().Object,
                resourceService.Object);

            viewModel.Start();

            Assert.Equal(4, viewModel.Teammates.Count);
            Assert.DoesNotContain(viewModel.Teammates,
                teammate => teammate.DisplayName == "Local");
            Assert.All(viewModel.Teammates,
                teammate => Assert.Equal(20, teammate.RecentResults.Count));
            Assert.True(viewModel.Teammates[0].RecentResults[0].IsWin);
            Assert.False(viewModel.Teammates[0].RecentResults[1].IsWin);

            viewModel.Stop();
        }

        [Fact]
        public void AutoPickToggle_WhenClicked_UpdatesSharedSettingAndCard()
        {
            var snapshot = CreateAutomationSnapshot(GameQueueIds.RankedSoloDuo);
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var automationSettings = CreateAutomationSettings();
            var autoPickEnabled = false;
            automationSettings.SetupGet(settings => settings.AutoPickChampion)
                .Returns(() => autoPickEnabled);
            automationSettings.SetupSet(settings =>
                    settings.AutoPickChampion = It.IsAny<bool>())
                .Callback((bool value) =>
                {
                    autoPickEnabled = value;
                    automationSettings.Raise(
                        settings => settings.Changed += null,
                        EventArgs.Empty);
                });
            var viewModel = CreateViewModel(
                matchService.Object,
                new Mock<IGameService>().Object,
                new Mock<IGameResourceManager>().Object,
                automationSettings.Object);

            viewModel.Start();
            var pickCard = Assert.Single(viewModel.AutomationCards,
                card => card.Label == "Auto Pick");

            Assert.True(pickCard.HasToggle);
            Assert.False(pickCard.IsEnabled);
            pickCard.ToggleCommand.Execute();

            Assert.True(autoPickEnabled);
            Assert.True(Assert.Single(viewModel.AutomationCards,
                card => card.Label == "Auto Pick").IsEnabled);
            Assert.False(Assert.Single(viewModel.AutomationCards,
                card => card.Label == "Auto Ban").HasToggle);
            viewModel.Stop();
        }

        [Fact]
        public void AramSwapToggle_WhenClicked_UpdatesSharedSettingAndCard()
        {
            var snapshot = CreateAutomationSnapshot(GameQueueIds.Aram);
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var automationSettings = CreateAutomationSettings();
            var autoSwapEnabled = false;
            automationSettings.SetupGet(settings => settings.AutoSwapAramBench)
                .Returns(() => autoSwapEnabled);
            automationSettings.SetupSet(settings =>
                    settings.AutoSwapAramBench = It.IsAny<bool>())
                .Callback((bool value) =>
                {
                    autoSwapEnabled = value;
                    automationSettings.Raise(
                        settings => settings.Changed += null,
                        EventArgs.Empty);
                });
            var viewModel = CreateViewModel(
                matchService.Object,
                new Mock<IGameService>().Object,
                new Mock<IGameResourceManager>().Object,
                automationSettings.Object);

            viewModel.Start();
            var swapCard = Assert.Single(viewModel.AutomationCards,
                card => card.Label == "Auto swap");

            Assert.True(swapCard.HasToggle);
            Assert.False(swapCard.IsEnabled);
            swapCard.ToggleCommand.Execute();

            Assert.True(autoSwapEnabled);
            Assert.True(Assert.Single(viewModel.AutomationCards,
                card => card.Label == "Auto swap").IsEnabled);
            Assert.False(Assert.Single(viewModel.AutomationCards,
                card => card.Label == "Current champion").HasToggle);
            viewModel.Stop();
        }

        [Fact]
        public void Start_InHextechAram_HidesRunesAndDoesNotRequestRecommendations()
        {
            var snapshot = CreateRuneSnapshot(GameQueueIds.HextechAram);
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var gameService = new Mock<IGameService>();
            var viewModel = CreateViewModel(
                matchService.Object,
                gameService.Object,
                new Mock<IGameResourceManager>().Object);

            viewModel.Start();

            Assert.False(viewModel.IsRuneRecommendationVisible);
            gameService.Verify(service => service.GetRuneRecommendationsAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
            viewModel.Stop();
        }

        [Fact]
        public void Start_WhenGameflowLooksLikeAramButLobbyIsHextech_HidesRunes()
        {
            var snapshot = CreateRuneSnapshot(GameQueueIds.Aram);
            snapshot.Lobby = new LobbySnapshot
            {
                GameConfig = new LobbyGameConfiguration
                {
                    QueueId = GameQueueIds.HextechAram,
                    GameMode = "ARAM",
                    MapId = 12
                }
            };
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var gameService = new Mock<IGameService>();
            var viewModel = CreateViewModel(
                matchService.Object,
                gameService.Object,
                new Mock<IGameResourceManager>().Object);

            viewModel.Start();

            Assert.False(viewModel.IsRuneRecommendationVisible);
            gameService.Verify(service => service.GetRuneRecommendationsAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
            viewModel.Stop();
        }

        [Fact]
        public void Start_InKiwiGameflowQueue_HidesRunesAndDoesNotRequestRecommendations()
        {
            var snapshot = CreateRuneSnapshot(GameQueueIds.HextechAramGameflow);
            snapshot.GameflowSession.GameData.GameMode = "KIWI";
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var gameService = new Mock<IGameService>();
            var viewModel = CreateViewModel(
                matchService.Object,
                gameService.Object,
                new Mock<IGameResourceManager>().Object);

            viewModel.Start();

            Assert.False(viewModel.IsRuneRecommendationVisible);
            gameService.Verify(service => service.GetRuneRecommendationsAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
            viewModel.Stop();
        }

        [Fact]
        public async Task Start_WithSelectedChampion_LoadsValidatedRuneRecommendation()
        {
            var snapshot = CreateRuneSnapshot(GameQueueIds.RankedSoloDuo);
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var gameService = new Mock<IGameService>();
            var recommendation = CreateRecommendationSet();
            gameService.Setup(service => service.GetRuneRecommendationsAsync(
                    103,
                    "mid",
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(recommendation);
            var appliedPageName = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            gameService.Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .Callback((string pageName,
                    RuneRecommendationOption _,
                    CancellationToken __) => appliedPageName.TrySetResult(pageName))
                .ReturnsAsync(new RunePageApplyResult
                {
                    Status = RunePageApplyStatus.Applied,
                    RunePageId = 42
                });
            var resourceManager = new Mock<IGameResourceManager>();
            resourceManager.Setup(service => service.GetPerksAsync())
                .ReturnsAsync(recommendation.Popular.SelectedPerkIds
                    .Distinct()
                    .Select(perkId => new Perk
                    {
                        Id = perkId,
                        Name = $"Perk {perkId}"
                    })
                    .ToList());
            resourceManager.Setup(service => service.GetPerkIconByIdAsync(
                    It.IsAny<int>()))
                .ReturnsAsync((int perkId) => $"{perkId}.png");
            resourceManager.Setup(service => service.GetChampionSummarysAsync())
                .ReturnsAsync(
                [
                    new ChampionSummary { Id = 103, Name = "Ahri" }
                ]);
            var viewModel = CreateViewModel(
                matchService.Object,
                gameService.Object,
                resourceManager.Object);

            viewModel.Start();
            await WaitUntilAsync(() => viewModel.HasRuneRecommendation);

            Assert.True(viewModel.IsRuneRecommendationVisible);
            Assert.True(viewModel.IsRuneRecommendationValid);
            Assert.Equal(9, viewModel.RunePerks.Count);
            Assert.Equal("Ahri · Mid", viewModel.RuneChampionText);
            Assert.True(viewModel.ApplyRuneCommand.CanExecute());
            viewModel.ApplyRuneCommand.Execute();
            Assert.Equal(
                "Ahri - Most popular runes [Prometheus]",
                await appliedPageName.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            viewModel.Stop();
        }

        [Fact]
        public async Task Start_WhenChampionNamesInitiallyUnavailable_RetriesBeforeApplying()
        {
            var snapshot = CreateRuneSnapshot(GameQueueIds.RankedSoloDuo);
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var recommendation = CreateRecommendationSet();
            var gameService = new Mock<IGameService>();
            gameService.Setup(service => service.GetRuneRecommendationsAsync(
                    103,
                    "mid",
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(recommendation);
            var appliedPageName = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            gameService.Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .Callback((string pageName,
                    RuneRecommendationOption _,
                    CancellationToken __) => appliedPageName.TrySetResult(pageName))
                .ReturnsAsync(new RunePageApplyResult
                {
                    Status = RunePageApplyStatus.Applied,
                    RunePageId = 42
                });
            var resourceManager = CreateRuneResourceManager(recommendation);
            resourceManager.SetupSequence(service => service.GetChampionSummarysAsync())
                .ReturnsAsync((List<ChampionSummary>)null)
                .ReturnsAsync(
                [
                    new ChampionSummary { Id = 103, Name = "Ahri" }
                ]);
            var viewModel = CreateViewModel(
                matchService.Object,
                gameService.Object,
                resourceManager.Object);

            viewModel.Start();
            await WaitUntilAsync(() => viewModel.HasRuneRecommendation);

            Assert.Equal("Ahri · Mid", viewModel.RuneChampionText);
            Assert.True(viewModel.ApplyRuneCommand.CanExecute());
            resourceManager.Verify(
                service => service.GetChampionSummarysAsync(),
                Times.Exactly(2));
            viewModel.ApplyRuneCommand.Execute();
            Assert.Equal(
                "Ahri - Most popular runes [Prometheus]",
                await appliedPageName.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            viewModel.Stop();
        }

        [Fact]
        public async Task Start_WhenChampionNameCannotBeResolved_DisablesRuneApplication()
        {
            var snapshot = CreateRuneSnapshot(GameQueueIds.RankedSoloDuo);
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var recommendation = CreateRecommendationSet();
            var gameService = new Mock<IGameService>();
            gameService.Setup(service => service.GetRuneRecommendationsAsync(
                    103,
                    "mid",
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(recommendation);
            var resourceManager = CreateRuneResourceManager(recommendation);
            resourceManager.Setup(service => service.GetChampionSummarysAsync())
                .ReturnsAsync((List<ChampionSummary>)null);
            var viewModel = CreateViewModel(
                matchService.Object,
                gameService.Object,
                resourceManager.Object);

            viewModel.Start();
            await WaitUntilAsync(() => viewModel.HasRuneRecommendation);

            Assert.Equal("#103 · Mid", viewModel.RuneChampionText);
            Assert.False(viewModel.ApplyRuneCommand.CanExecute());
            Assert.Equal("Unable to resolve champion name", viewModel.RuneStatusText);
            gameService.Verify(service => service.ApplyRuneRecommendationAsync(
                It.IsAny<string>(),
                It.IsAny<RuneRecommendationOption>(),
                It.IsAny<CancellationToken>()), Times.Never);
            viewModel.Stop();
        }

        private static LcuCompanionViewModel CreateViewModel(
            IMatchService matchService,
            IGameService gameService,
            IGameResourceManager gameResourceManager)
        {
            var automationSettings = CreateAutomationSettings();
            return CreateViewModel(
                matchService,
                gameService,
                gameResourceManager,
                automationSettings.Object);
        }

        private static LcuCompanionViewModel CreateViewModel(
            IMatchService matchService,
            IGameService gameService,
            IGameResourceManager gameResourceManager,
            IGameAutomationSettings automationSettings)
        {
            var resourceService = new Mock<IResourceService>();
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string _) => null);
            return new LcuCompanionViewModel(
                new EventAggregator(),
                matchService,
                gameService,
                automationSettings,
                gameResourceManager,
                resourceService.Object);
        }

        private static Mock<IGameAutomationSettings> CreateAutomationSettings()
        {
            var automationSettings = new Mock<IGameAutomationSettings>();
            automationSettings.SetupGet(settings => settings.PreferredPickChampionIds)
                .Returns([103]);
            automationSettings.SetupGet(settings => settings.PreferredBanChampionIds)
                .Returns([]);
            automationSettings.SetupGet(settings => settings.PreferredAramChampionIds)
                .Returns([103]);
            return automationSettings;
        }

        private static Mock<IGameResourceManager> CreateRuneResourceManager(
            RuneRecommendationSet recommendation)
        {
            var resourceManager = new Mock<IGameResourceManager>();
            resourceManager.Setup(service => service.GetPerksAsync())
                .ReturnsAsync(recommendation.Popular.SelectedPerkIds
                    .Distinct()
                    .Select(perkId => new Perk
                    {
                        Id = perkId,
                        Name = $"Perk {perkId}"
                    })
                    .ToList());
            resourceManager.Setup(service => service.GetPerkIconByIdAsync(
                    It.IsAny<int>()))
                .ReturnsAsync((int perkId) => $"{perkId}.png");
            return resourceManager;
        }

        private static LiveMatchSnapshot CreateRuneSnapshot(int queueId)
        {
            return new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.ChampSelect,
                GameflowSession = new GameflowSessionSnapshot
                {
                    GameData = new GameflowGameData
                    {
                        QueueId = queueId,
                        GameMode = queueId is GameQueueIds.Aram or
                            GameQueueIds.HextechAram or GameQueueIds.HextechAramGameflow
                            ? "ARAM"
                            : "CLASSIC",
                        MapId = queueId is GameQueueIds.Aram or
                            GameQueueIds.HextechAram or GameQueueIds.HextechAramGameflow
                            ? 12
                            : 11
                    }
                },
                ChampionSelect = new ChampionSelectSnapshot
                {
                    LocalPlayerCellId = 1,
                    MyTeam =
                    [
                        new ChampionSelectTeamMemberSnapshot
                        {
                            CellId = 1,
                            ChampionId = 103,
                            AssignedPosition = "middle"
                        }
                    ]
                }
            };
        }

        private static LiveMatchSnapshot CreateAutomationSnapshot(int queueId)
        {
            var isAram = queueId is GameQueueIds.Aram or
                GameQueueIds.HextechAram or GameQueueIds.HextechAramGameflow;
            return new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.ChampSelect,
                GameflowSession = new GameflowSessionSnapshot
                {
                    GameData = new GameflowGameData
                    {
                        QueueId = queueId,
                        GameMode = isAram ? "ARAM" : "CLASSIC",
                        MapId = isAram ? 12 : 11
                    }
                },
                ChampionSelect = new ChampionSelectSnapshot
                {
                    LocalPlayerCellId = 1,
                    BenchEnabled = isAram,
                    BenchChampions = isAram
                        ?
                        [
                            new ChampionSelectBenchChampionSnapshot
                            {
                                ChampionId = 103
                            }
                        ]
                        : []
                }
            };
        }

        private static RuneRecommendationSet CreateRecommendationSet()
        {
            var option = new RuneRecommendationOption
            {
                PrimaryStyleId = 8100,
                SubStyleId = 8200,
                SelectedPerkIds =
                [
                    8112, 8139, 8140, 8106, 8210, 8226, 5005, 5008, 5001
                ],
                SampleCount = 1000,
                PickRateBasisPoints = 5000,
                WinRateBasisPoints = 5100
            };
            return new RuneRecommendationSet
            {
                ChampionId = 103,
                Lane = "mid",
                Source = "QQ",
                DataVersion = "16.15",
                Popular = option,
                WinRate = option
            };
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.True(condition());
        }
    }
}
