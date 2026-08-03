using Moq;
using Prism.Ioc;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Shared.ViewModels;
using Xunit;
using MatchModel = Prometheus.Core.Models.Match;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class SummonerDetailViewModelTests
    {
        [Fact]
        public async Task OnNavigatedTo_WhenBackdropFails_StillLoadsAvailableRanks()
        {
            using var context = new TestContext();
            context.SummonerService.Setup(service => service.GetBackdorpByIdAsync(
                    123, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Backdrop unavailable"));
            context.SummonerService.Setup(service => service.GetRankStatsByPuuid(
                    "player-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    {
                      "queueMap": {
                        "RANKED_SOLO_5x5": {
                          "tier": "GOLD",
                          "division": "II",
                          "leaguePoints": 45,
                          "wins": 10,
                          "losses": 8,
                          "queueType": "RANKED_SOLO_5x5"
                        }
                      }
                    }
                    """);

            await context.NavigateAsync();

            Assert.Equal("background-0.jpg", context.ViewModel.BackgroundSkin);
            Assert.Equal(Tier.GOLD, context.ViewModel.Solo.Tier);
            Assert.Equal("II", context.ViewModel.Solo.Division);
            Assert.Equal(Tier.UNRANKED, context.ViewModel.Flex.Tier);
            Assert.Equal(QueueType.RANKED_FLEX_SR, context.ViewModel.Flex.QueueType);
            context.SummonerService.Verify(service => service.GetRankStatsByPuuid(
                "player-puuid", It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetMatchHistoryAsync(
                "player-puuid", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnNavigatedTo_WhenRankResponseIsMalformed_ShowsUnrankedAndLoadsHistory()
        {
            using var context = new TestContext();
            context.SummonerService.Setup(service => service.GetBackdorpByIdAsync(
                    123, It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    {
                      "backdropImage": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin27/Images/ahri_splash_centered_27.jpg",
                      "backdropType": "specified-skin",
                      "championId": 103
                    }
                    """);
            context.SummonerService.Setup(service => service.GetRankStatsByPuuid(
                    "player-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync("not-json");

            await context.NavigateAsync();

            Assert.Equal("background-103027.jpg", context.ViewModel.BackgroundSkin);
            Assert.Equal(Tier.UNRANKED, context.ViewModel.Solo.Tier);
            Assert.Equal(Tier.UNRANKED, context.ViewModel.Flex.Tier);
            Assert.NotNull(context.ViewModel.RecentMatches);
            Assert.False(context.ViewModel.IsLoading);
            context.SummonerService.Verify(service => service.GetMatchHistoryAsync(
                "player-puuid", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnNavigatedTo_WhenAccountChanges_DoesNotApplyThePreviousLoad()
        {
            using var context = new TestContext();
            var oldRankStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var oldRankCompletion = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.SummonerService.Setup(service => service.GetRankStatsByPuuid(
                    "old-puuid", It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    oldRankStarted.TrySetResult(true);
                    return oldRankCompletion.Task;
                });
            context.SummonerService.Setup(service => service.GetRankStatsByPuuid(
                    "new-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    {
                      "queueMap": {
                        "RANKED_SOLO_5x5": {
                          "tier": "DIAMOND",
                          "queueType": "RANKED_SOLO_5x5"
                        }
                      }
                    }
                    """);
            context.SummonerService.Setup(service => service.GetMatchHistoryAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryQueryResult
                {
                    Succeeded = true,
                    Matches = []
                });

            context.Navigate(CreateSummoner("old-puuid"));
            await oldRankStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            context.Navigate(CreateSummoner("new-puuid"));
            await WaitForIdleAsync(context.ViewModel);

            oldRankCompletion.TrySetResult("""
                {
                  "queueMap": {
                    "RANKED_SOLO_5x5": {
                      "tier": "BRONZE",
                      "queueType": "RANKED_SOLO_5x5"
                    }
                  }
                }
                """);
            await Task.Delay(50);

            Assert.Equal("new-puuid", context.ViewModel.Summoner.Puuid);
            Assert.Equal(Tier.DIAMOND, context.ViewModel.Solo.Tier);
        }

        [Fact]
        public async Task SelectedMatchTypeIndex_WhenChanged_FiltersMatchesByQueue()
        {
            using var context = new TestContext();
            context.SummonerService.Setup(service => service.GetMatchHistoryAsync(
                    "player-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryQueryResult
                {
                    Succeeded = true,
                    Matches =
                    [
                        CreateMatch(1, GameQueueIds.Aram, "ARAM"),
                        CreateMatch(2, GameQueueIds.NormalDraft, "CLASSIC"),
                        CreateMatch(3, GameQueueIds.RankedSoloDuo, "CLASSIC"),
                        CreateMatch(4, GameQueueIds.RankedFlex, "CLASSIC"),
                        CreateMatch(5, 1700, "CHERRY")
                    ]
                });

            await context.NavigateAsync();

            AssertVisibleMatches(context.ViewModel, 1, 2, 3, 4, 5);

            context.ViewModel.SelectedMatchTypeIndex = 1;
            AssertVisibleMatches(context.ViewModel, 1);

            context.ViewModel.SelectedMatchTypeIndex = 2;
            AssertVisibleMatches(context.ViewModel, 2);

            context.ViewModel.SelectedMatchTypeIndex = 3;
            AssertVisibleMatches(context.ViewModel, 3);

            context.ViewModel.SelectedMatchTypeIndex = 4;
            AssertVisibleMatches(context.ViewModel, 4);

            context.ViewModel.SelectedMatchTypeIndex = 0;
            AssertVisibleMatches(context.ViewModel, 1, 2, 3, 4, 5);
        }

        [Fact]
        public async Task MoreMatchCommand_WhenHostedBySearch_NavigatesWithinSearchRegion()
        {
            using var context = new TestContext();
            context.Navigate(CreateSummoner("player-puuid"),
                RegionNames.SearchContent, false);
            await WaitForIdleAsync(context.ViewModel);

            context.ViewModel.MoreMatchCommand.Execute();

            Assert.False(context.ViewModel.ShowPageHeader);
            context.RegionManager.Verify(manager => manager.RequestNavigate(
                    RegionNames.SearchContent,
                    RegionNames.MatchHistoryView,
                    It.Is<NavigationParameters>(parameters =>
                        Equals(parameters[ParameterNames.HostRegionName],
                            RegionNames.SearchContent))),
                Times.Once);
        }

        private static MatchModel CreateMatch(long gameId, int queueId, string gameMode)
        {
            return new MatchModel
            {
                GameId = gameId,
                QueueId = queueId,
                GameMode = gameMode,
                Participants =
                [
                    new Participant
                    {
                        ChampionId = 22,
                        Stats = new MatchStats()
                    }
                ]
            };
        }

        private static void AssertVisibleMatches(
            SummonerDetailViewModel viewModel, params long[] expectedGameIds)
        {
            Assert.Equal(expectedGameIds,
                viewModel.RecentMatches.Cast<MatchModel>().Select(match => match.GameId));
        }

        private static SummonerAccount CreateSummoner(string puuid)
        {
            return new SummonerAccount
            {
                Puuid = puuid,
                SummonerId = 123,
                ProfileIconId = 7,
                Privacy = "PUBLIC"
            };
        }

        private static async Task WaitForIdleAsync(SummonerDetailViewModel viewModel)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (!viewModel.IsLoading)
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.False(viewModel.IsLoading, "The view model did not finish loading in time.");
        }

        private sealed class TestContext : IDisposable
        {
            public TestContext()
            {
                Container.Setup(extension => extension.Resolve(typeof(IResourceService)))
                    .Returns(ResourceService.Object);
                Container.Setup(extension => extension.Resolve(typeof(ISummonerService)))
                    .Returns(SummonerService.Object);
                Container.Setup(extension => extension.Resolve(typeof(IGameResourceManager)))
                    .Returns(GameResourceManager.Object);
                Container.Setup(extension => extension.Resolve(typeof(IDialogService)))
                    .Returns(DialogService.Object);
                ResourceService.Setup(service => service.GetTierIconResourceUri(
                        It.IsAny<string>()))
                    .Returns((string tier) => $"rank-{tier}.png");
                GameResourceManager.Setup(service => service.GetBackgroundSkinByIdAsync(
                        It.IsAny<int>()))
                    .ReturnsAsync((int skinId) => $"background-{skinId}.jpg");
                GameResourceManager.Setup(service => service.GetProfileIconByIdAsync(7))
                    .ReturnsAsync("profile-7.jpg");
                GameResourceManager.Setup(service => service.GetChampoinIconByIdAsync(
                        It.IsAny<int>()))
                    .ReturnsAsync((int championId) => $"champion-{championId}.png");
                SummonerService.Setup(service => service.GetMatchHistoryAsync(
                        "player-puuid", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new MatchHistoryQueryResult
                    {
                        Succeeded = true,
                        Matches = []
                    });
                ViewModel = new SummonerDetailViewModel(
                    RegionManager.Object, Container.Object);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public Mock<IContainerExtension> Container { get; } = new();

            public Mock<IResourceService> ResourceService { get; } = new();

            public Mock<ISummonerService> SummonerService { get; } = new();

            public Mock<IGameResourceManager> GameResourceManager { get; } = new();

            public Mock<IDialogService> DialogService { get; } = new();

            public Mock<IRegionNavigationService> NavigationService { get; } = new();

            public SummonerDetailViewModel ViewModel { get; }

            public async Task NavigateAsync()
            {
                Navigate(CreateSummoner("player-puuid"));
                await WaitForIdleAsync(ViewModel);
            }

            public void Navigate(SummonerAccount summoner,
                string hostRegionName = RegionNames.SummonerContent,
                bool showPageHeader = true)
            {
                var parameters = new NavigationParameters
                {
                    { ParameterNames.Summoner, summoner },
                    { ParameterNames.CanEdit, false },
                    { ParameterNames.HostRegionName, hostRegionName },
                    { ParameterNames.ShowPageHeader, showPageHeader }
                };
                var navigationContext = new NavigationContext(
                    NavigationService.Object,
                    new Uri(RegionNames.SummonerDetailView, UriKind.Relative),
                    parameters);
                typeof(NavigationContext)
                    .GetProperty(nameof(NavigationContext.Parameters))
                    ?.SetValue(navigationContext, parameters);

                ViewModel.OnNavigatedTo(navigationContext);
            }

            public void Dispose()
            {
                ViewModel.Destroy();
            }
        }
    }
}
