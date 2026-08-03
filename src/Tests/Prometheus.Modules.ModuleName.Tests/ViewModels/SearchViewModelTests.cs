using Moq;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Modules.Search.ViewModels;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class SearchViewModelTests
    {
        [Fact]
        public async Task SearchCommand_WhenSummonerFound_NavigatesInsideSearchPage()
        {
            var regionManager = new Mock<IRegionManager>();
            var summonerService = new Mock<ISummonerService>();
            var summoner = CreateSummoner("searched-puuid", "Visible Player", "CN1");
            summonerService.Setup(service => service.SearchSummonerByName(
                    "Visible Player#CN1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(summoner);
            var viewModel = new SearchViewModel(regionManager.Object,
                summonerService.Object)
            {
                SearchText = "Visible Player#CN1"
            };

            viewModel.SearchCommand.Execute();
            await WaitUntilAsync(() => !viewModel.IsSearching && viewModel.HasResult);

            Assert.False(viewModel.ShowNotFound);
            regionManager.Verify(manager => manager.RequestNavigate(
                    RegionNames.SearchContent,
                    RegionNames.SummonerDetailView,
                    It.Is<NavigationParameters>(parameters =>
                        ReferenceEquals(parameters[ParameterNames.Summoner], summoner) &&
                        Equals(parameters[ParameterNames.HostRegionName],
                            RegionNames.SearchContent) &&
                        Equals(parameters[ParameterNames.ShowPageHeader], false))),
                Times.Once);
        }

        [Fact]
        public async Task SearchCommand_WhenSearchIsReplaced_OnlyLatestResultNavigates()
        {
            var regionManager = new Mock<IRegionManager>();
            var summonerService = new Mock<ISummonerService>();
            var firstCompletion = new TaskCompletionSource<SummonerAccount>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            summonerService.Setup(service => service.SearchSummonerByName(
                    "First#CN1", It.IsAny<CancellationToken>()))
                .Returns(firstCompletion.Task);
            var latestSummoner = CreateSummoner("latest-puuid", "Latest", "CN1");
            summonerService.Setup(service => service.SearchSummonerByName(
                    "Latest#CN1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(latestSummoner);
            var viewModel = new SearchViewModel(regionManager.Object,
                summonerService.Object)
            {
                SearchText = "First#CN1"
            };

            viewModel.SearchCommand.Execute();
            await WaitUntilAsync(() => viewModel.IsSearching);
            viewModel.SearchText = "Latest#CN1";
            viewModel.SearchCommand.Execute();
            await WaitUntilAsync(() => !viewModel.IsSearching && viewModel.HasResult);
            firstCompletion.TrySetResult(CreateSummoner("first-puuid", "First", "CN1"));
            await Task.Delay(30);

            regionManager.Verify(manager => manager.RequestNavigate(
                    RegionNames.SearchContent,
                    RegionNames.SummonerDetailView,
                    It.IsAny<NavigationParameters>()),
                Times.Once);
            var navigationInvocation = regionManager.Invocations.Single(
                invocation => invocation.Method.Name ==
                    nameof(IRegionManager.RequestNavigate));
            var parameters = Assert.IsType<NavigationParameters>(
                navigationInvocation.Arguments[2]);
            var navigatedSummoner = Assert.IsType<SummonerAccount>(
                parameters[ParameterNames.Summoner]);
            Assert.Equal("latest-puuid", navigatedSummoner.Puuid);
        }

        [Fact]
        public async Task SearchCommand_WhenSummonerMissing_ShowsNotFoundWithoutNavigation()
        {
            var regionManager = new Mock<IRegionManager>();
            var summonerService = new Mock<ISummonerService>();
            summonerService.Setup(service => service.SearchSummonerByName(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SummonerAccount)null);
            var viewModel = new SearchViewModel(regionManager.Object,
                summonerService.Object)
            {
                SearchText = "Missing#CN1"
            };

            viewModel.SearchCommand.Execute();
            await WaitUntilAsync(() => !viewModel.IsSearching);

            Assert.True(viewModel.ShowNotFound);
            Assert.False(viewModel.HasResult);
            Assert.DoesNotContain(regionManager.Invocations,
                invocation => invocation.Method.Name == nameof(IRegionManager.RequestNavigate));
        }

        private static SummonerAccount CreateSummoner(string puuid,
            string gameName, string tagLine)
        {
            return new SummonerAccount
            {
                Puuid = puuid,
                GameName = gameName,
                TagLine = tagLine
            };
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (predicate())
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.True(predicate(), "The expected search state was not reached in time.");
        }
    }
}
