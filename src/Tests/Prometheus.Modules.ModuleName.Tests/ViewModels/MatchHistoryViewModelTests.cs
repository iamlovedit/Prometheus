using Moq;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Shared.ViewModels;
using Xunit;
using MatchModel = Prometheus.Core.Models.Match;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class MatchHistoryViewModelTests
    {
        [Fact]
        public async Task OnNavigatedTo_LoadsTwentyRecentMatches()
        {
            var history = CreateMatches(1, 20);
            using var context = new TestContext();
            context.SetupHistory("test-puuid", history);

            await context.NavigateAsync("test-puuid");

            Assert.Equal(history.Select(match => match.GameId),
                context.ViewModel.Matches.Select(match => match.GameId));
            Assert.False(context.ViewModel.ShowLoadError);
            context.SummonerService.Verify(service => service.GetMatchHistoryAsync(
                "test-puuid", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnNavigatedTo_WhenHistoryQueryFails_ShowsError()
        {
            using var context = new TestContext();
            context.SummonerService.Setup(service => service.GetMatchHistoryAsync(
                    "test-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryQueryResult
                {
                    Succeeded = false,
                    Error = "LCU is unavailable."
                });

            await context.NavigateAsync("test-puuid");

            Assert.Empty(context.ViewModel.Matches);
            Assert.True(context.ViewModel.ShowLoadError);
        }

        [Fact]
        public async Task OnNavigatedTo_WhenViewModelIsReused_ResetsLoadedHistory()
        {
            using var context = new TestContext();
            context.SetupHistory("first-puuid", CreateMatches(1, 20));
            await context.NavigateAsync("first-puuid");

            var newHistory = CreateMatches(101, 5);
            context.SetupHistory("second-puuid", newHistory);
            await context.NavigateAsync("second-puuid");

            Assert.Equal(newHistory.Select(match => match.GameId),
                context.ViewModel.Matches.Select(match => match.GameId));
            Assert.False(context.ViewModel.ShowLoadError);
        }

        private static List<MatchModel> CreateMatches(int firstGameId, int count)
        {
            return Enumerable.Range(firstGameId, count)
                .Select(gameId => new MatchModel
                {
                    GameId = gameId,
                    GameCreation = 1_753_958_400_000 - gameId * 60_000L,
                    Participants =
                    [
                        new Participant
                        {
                            ChampionId = gameId,
                            Stats = new MatchStats()
                        }
                    ]
                })
                .ToList();
        }

        private static async Task WaitForIdleAsync(MatchHistoryViewModel viewModel)
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
                GameService.Setup(service => service.GetMatchDetailAsync(
                        It.IsAny<long>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((MatchDetail)null);
                GameResourceManager.Setup(service => service.GetChampoinIconByIdAsync(
                        It.IsAny<int>()))
                    .ReturnsAsync((int championId) => $"{championId}.png");
                ViewModel = new MatchHistoryViewModel(
                    RegionManager.Object,
                    GameService.Object,
                    GameResourceManager.Object,
                    SummonerService.Object);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public Mock<IGameService> GameService { get; } = new();

            public Mock<IGameResourceManager> GameResourceManager { get; } = new();

            public Mock<ISummonerService> SummonerService { get; } = new();

            public Mock<IRegionNavigationService> NavigationService { get; } = new();

            public MatchHistoryViewModel ViewModel { get; }

            public void SetupHistory(string puuid, IReadOnlyList<MatchModel> matches)
            {
                SummonerService.Setup(service => service.GetMatchHistoryAsync(
                        puuid, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new MatchHistoryQueryResult
                    {
                        Succeeded = true,
                        Matches = matches
                    });
            }

            public async Task NavigateAsync(string puuid, MatchModel selectedMatch = null)
            {
                var parameters = new NavigationParameters
                {
                    { ParameterNames.Summoner, new SummonerAccount { Puuid = puuid } },
                    { ParameterNames.CanEdit, false }
                };
                if (selectedMatch is not null)
                {
                    parameters.Add(ParameterNames.SelectedMatch, selectedMatch);
                }
                var context = new NavigationContext(
                    NavigationService.Object,
                    new Uri(RegionNames.MatchHistoryView, UriKind.Relative),
                    parameters);
                typeof(NavigationContext)
                    .GetProperty(nameof(NavigationContext.Parameters))
                    ?.SetValue(context, parameters);

                ViewModel.OnNavigatedTo(context);
                await WaitForIdleAsync(ViewModel);
            }

            public void Dispose()
            {
                ViewModel.Destroy();
            }
        }
    }
}
