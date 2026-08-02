using Moq;
using Prism.Ioc;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Shared.ViewModels;
using Xunit;

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
                var parameters = new NavigationParameters
                {
                    {
                        ParameterNames.Summoner,
                        new SummonerAccount
                        {
                            Puuid = "player-puuid",
                            SummonerId = 123,
                            ProfileIconId = 7,
                            Privacy = "PUBLIC"
                        }
                    },
                    { ParameterNames.CanEdit, false }
                };
                var navigationContext = new NavigationContext(
                    NavigationService.Object,
                    new Uri(RegionNames.SummonerDetailView, UriKind.Relative),
                    parameters);
                typeof(NavigationContext)
                    .GetProperty(nameof(NavigationContext.Parameters))
                    ?.SetValue(navigationContext, parameters);

                ViewModel.OnNavigatedTo(navigationContext);
                await WaitForIdleAsync(ViewModel);
            }

            public void Dispose()
            {
                ViewModel.Destroy();
            }
        }
    }
}
