using Moq;
using Prism.Events;
using Prism.Ioc;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Modules.Summoner.ViewModels;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class SummonerViewModelTests
    {
        [Fact]
        public async Task OnNavigatedTo_WhenReentered_LoadsTheLatestCurrentSummoner()
        {
            using var context = new TestContext();
            context.SummonerService
                .SetupSequence(service => service.GetCurrentSummoner(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSummoner("old-puuid", 101))
                .ReturnsAsync(CreateSummoner("new-puuid", 202));

            context.NavigateToCurrentSummoner();
            await context.WaitForNavigationCountAsync(1);
            context.ViewModel.OnNavigatedFrom(context.CreateNavigationContext());

            context.NavigateToCurrentSummoner();
            await context.WaitForNavigationCountAsync(2);

            var parameters = context.GetNavigationParameters(1);
            Assert.True(parameters.TryGetValue<SummonerAccount>(
                ParameterNames.Summoner, out var summoner));
            Assert.Equal("new-puuid", summoner.Puuid);
            Assert.True(parameters.TryGetValue<bool>(ParameterNames.CanEdit,
                out var canEdit));
            Assert.True(canEdit);
            context.SummonerService.Verify(service => service.GetCurrentSummoner(
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task CurrentSummonerEvent_WhenViewingOwnCareer_RefreshesImmediately()
        {
            using var context = new TestContext();
            context.SummonerService
                .SetupSequence(service => service.GetCurrentSummoner(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSummoner("old-puuid", 101))
                .ReturnsAsync(CreateSummoner("new-puuid", 202));

            context.NavigateToCurrentSummoner();
            await context.WaitForNavigationCountAsync(1);

            context.RaiseCurrentSummonerChanged();
            await context.WaitForNavigationCountAsync(2);

            var parameters = context.GetNavigationParameters(1);
            Assert.True(parameters.TryGetValue<SummonerAccount>(
                ParameterNames.Summoner, out var summoner));
            Assert.Equal("new-puuid", summoner.Puuid);
            Assert.True(parameters.TryGetValue<bool>(ParameterNames.CanEdit,
                out var canEdit));
            Assert.True(canEdit);
        }

        private static SummonerAccount CreateSummoner(string puuid, long summonerId)
        {
            return new SummonerAccount
            {
                Puuid = puuid,
                SummonerId = summonerId
            };
        }

        private sealed class TestContext : IDisposable
        {
            private Action<OnWebsocketEventArgs> _currentSummonerChanged;

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
                Container.Setup(extension => extension.Resolve(typeof(ILeagueClient)))
                    .Returns(LeagueClient.Object);
                LeagueClient.Setup(client => client.Subscribe(
                        "/lol-summoner/v1/current-summoner",
                        It.IsAny<Action<OnWebsocketEventArgs>>()))
                    .Callback<string, Action<OnWebsocketEventArgs>>((_, callback) =>
                        _currentSummonerChanged = callback);

                ViewModel = new SummonerViewModel(RegionManager.Object,
                    EventAggregator, Container.Object);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public IEventAggregator EventAggregator { get; } = new EventAggregator();

            public Mock<IContainerExtension> Container { get; } = new();

            public Mock<IResourceService> ResourceService { get; } = new();

            public Mock<ISummonerService> SummonerService { get; } = new();

            public Mock<IGameResourceManager> GameResourceManager { get; } = new();

            public Mock<IDialogService> DialogService { get; } = new();

            public Mock<ILeagueClient> LeagueClient { get; } = new();

            public Mock<IRegionNavigationService> NavigationService { get; } = new();

            public SummonerViewModel ViewModel { get; }

            public void NavigateToCurrentSummoner()
            {
                ViewModel.OnNavigatedTo(CreateNavigationContext());
            }

            public NavigationContext CreateNavigationContext()
            {
                var parameters = new NavigationParameters
                {
                    { ParameterNames.Summoner, null }
                };
                var navigationContext = new NavigationContext(
                    NavigationService.Object,
                    new Uri(MenuName.Career.ToString(), UriKind.Relative),
                    parameters);
                typeof(NavigationContext)
                    .GetProperty(nameof(NavigationContext.Parameters))
                    ?.SetValue(navigationContext, parameters);
                return navigationContext;
            }

            public void RaiseCurrentSummonerChanged()
            {
                Assert.NotNull(_currentSummonerChanged);
                _currentSummonerChanged(new OnWebsocketEventArgs
                {
                    Uri = "/lol-summoner/v1/current-summoner",
                    EventType = "Update"
                });
            }

            public async Task WaitForNavigationCountAsync(int expectedCount)
            {
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    if (RegionManager.Invocations.Count >= expectedCount)
                    {
                        return;
                    }

                    await Task.Delay(10);
                }

                Assert.True(RegionManager.Invocations.Count >= expectedCount,
                    $"Expected at least {expectedCount} navigation calls, but observed " +
                    $"{RegionManager.Invocations.Count}.");
            }

            public NavigationParameters GetNavigationParameters(int invocationIndex)
            {
                return Assert.IsType<NavigationParameters>(RegionManager.Invocations[
                    invocationIndex].Arguments.OfType<NavigationParameters>().Single());
            }

            public void Dispose()
            {
                ViewModel.Destroy();
                LeagueClient.Verify(client => client.Unsubscribe(
                    "/lol-summoner/v1/current-summoner",
                    It.IsAny<Action<OnWebsocketEventArgs>>()), Times.Once);
            }
        }
    }
}
