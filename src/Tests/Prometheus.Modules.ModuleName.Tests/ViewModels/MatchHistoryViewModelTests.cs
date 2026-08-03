using Moq;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Shared.Models;
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

        [Fact]
        public async Task SummonerCommand_WhenOpenedFromCareer_ShowsTwentyResultBubbles()
        {
            using var context = new TestContext();
            context.SetupHistory("test-puuid", []);
            await context.NavigateAsync("test-puuid");
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "other-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SummonerAccount
                {
                    Puuid = "other-puuid",
                    GameName = "Other Player",
                    TagLine = "CN1",
                    ProfileIconId = 7,
                    SummonerLevel = 286
                });
            context.SummonerService.Setup(service => service.GetRankStatsByPuuid(
                    "other-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    {
                      "queueMap": {
                        "RANKED_SOLO_5x5": {
                          "tier": "DIAMOND",
                          "division": "IV",
                          "leaguePoints": 42,
                          "wins": 53,
                          "losses": 47,
                          "queueType": "RANKED_SOLO_5x5"
                        }
                      }
                    }
                    """);
            context.SetupHistory("other-puuid", CreatePreviewMatches());

            context.ViewModel.SummonerCommand.Execute(new Player
            {
                Puuid = "other-puuid"
            });
            await WaitForPreviewAsync(context.ViewModel);

            Assert.True(context.ViewModel.IsPreviewOpen);
            Assert.False(context.ViewModel.ShowPreviewError);
            Assert.Equal(20, context.ViewModel.Preview.MatchCount);
            Assert.Equal(12, context.ViewModel.Preview.Wins);
            Assert.Equal(8, context.ViewModel.Preview.Losses);
            Assert.Equal("60%", context.ViewModel.Preview.WinRate);
            Assert.Equal(20, context.ViewModel.Preview.Results.Count);
            Assert.False(context.ViewModel.Preview.Results[0].IsWin);
            Assert.True(context.ViewModel.Preview.Results[^1].IsWin);
            Assert.Equal(Tier.DIAMOND, context.ViewModel.Preview.Solo.Tier);
        }

        [Fact]
        public async Task SummonerCommand_WhenPreviewHistoryFails_ShowsSafeError()
        {
            using var context = new TestContext();
            context.SetupHistory("test-puuid", []);
            await context.NavigateAsync("test-puuid");
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "other-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SummonerAccount
                {
                    Puuid = "other-puuid",
                    GameName = "Other Player",
                    TagLine = "CN1"
                });
            context.SummonerService.Setup(service => service.GetMatchHistoryAsync(
                    "other-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryQueryResult
                {
                    Succeeded = false,
                    Error = "LCU is unavailable."
                });

            context.ViewModel.SummonerCommand.Execute(new Player
            {
                Puuid = "other-puuid"
            });
            await WaitUntilAsync(() => context.ViewModel.IsPreviewOpen &&
                !context.ViewModel.IsPreviewLoading);

            Assert.True(context.ViewModel.ShowPreviewError);
            Assert.Null(context.ViewModel.Preview);
            Assert.True(context.ViewModel.IsPreviewOpen);
        }

        [Fact]
        public async Task ClosePreviewCommand_CancelsPendingPlayerLookup()
        {
            using var context = new TestContext();
            context.SetupHistory("test-puuid", []);
            await context.NavigateAsync("test-puuid");
            var completion = new TaskCompletionSource<SummonerAccount>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var lookupToken = CancellationToken.None;
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "other-puuid", It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken cancellationToken) =>
                {
                    lookupToken = cancellationToken;
                    return completion.Task;
                });

            context.ViewModel.SummonerCommand.Execute(new Player
            {
                Puuid = "other-puuid"
            });
            await WaitUntilAsync(() => context.ViewModel.IsPreviewOpen &&
                context.ViewModel.IsPreviewLoading);
            context.ViewModel.ClosePreviewCommand.Execute();

            Assert.True(lookupToken.IsCancellationRequested);
            Assert.False(context.ViewModel.IsPreviewOpen);
            Assert.Null(context.ViewModel.Preview);
            completion.TrySetResult(new SummonerAccount
            {
                Puuid = "other-puuid"
            });
            await Task.Delay(20);
            Assert.Null(context.ViewModel.Preview);
        }

        [Fact]
        public async Task SummonerCommand_WhenSelectionChanges_OnlyLatestPreviewIsApplied()
        {
            using var context = new TestContext();
            context.SetupHistory("test-puuid", []);
            await context.NavigateAsync("test-puuid");
            var firstCompletion = new TaskCompletionSource<SummonerAccount>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstLookupToken = CancellationToken.None;
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "first-puuid", It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken cancellationToken) =>
                {
                    firstLookupToken = cancellationToken;
                    return firstCompletion.Task;
                });
            var latest = new SummonerAccount
            {
                Puuid = "latest-puuid",
                GameName = "Latest Player",
                TagLine = "CN1"
            };
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "latest-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(latest);
            context.SetupHistory("latest-puuid", []);

            context.ViewModel.SummonerCommand.Execute(new Player
            {
                Puuid = "first-puuid"
            });
            await WaitUntilAsync(() => context.ViewModel.IsPreviewLoading);
            context.ViewModel.SummonerCommand.Execute(new Player
            {
                Puuid = "latest-puuid"
            });
            await WaitForPreviewAsync(context.ViewModel);

            Assert.True(firstLookupToken.IsCancellationRequested);
            Assert.Same(latest, context.ViewModel.Preview.Summoner);
            firstCompletion.TrySetResult(new SummonerAccount
            {
                Puuid = "first-puuid",
                GameName = "First Player",
                TagLine = "CN1"
            });
            await Task.Delay(20);
            Assert.Same(latest, context.ViewModel.Preview.Summoner);
        }

        [Fact]
        public async Task ViewFullRecordCommand_PublishesSummonerForSearchPage()
        {
            using var context = new TestContext();
            context.SetupHistory("test-puuid", []);
            await context.NavigateAsync("test-puuid");
            var target = new SummonerAccount
            {
                Puuid = "other-puuid",
                GameName = "Other Player",
                TagLine = "CN1",
                ProfileIconId = 7
            };
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "other-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(target);
            context.SetupHistory("other-puuid", CreatePreviewMatches());
            SummonerAccount published = null;
            context.EventAggregator.GetEvent<SearchSummonerEvent>()
                .Subscribe(summoner => published = summoner);

            context.ViewModel.SummonerCommand.Execute(new Player
            {
                Puuid = "other-puuid"
            });
            await WaitForPreviewAsync(context.ViewModel);
            context.ViewModel.ViewFullRecordCommand.Execute();

            Assert.Same(target, published);
            Assert.False(context.ViewModel.IsPreviewOpen);
        }

        [Fact]
        public async Task SummonerCommand_WhenOpenedFromSearch_NavigatesWithinSearchRegion()
        {
            using var context = new TestContext();
            context.SetupHistory("test-puuid", []);
            await context.NavigateAsync("test-puuid",
                hostRegionName: RegionNames.SearchContent);
            var target = new SummonerAccount
            {
                Puuid = "other-puuid",
                GameName = "Other Player",
                TagLine = "CN1"
            };
            context.SummonerService.Setup(service => service.SearchSummonerByPuuid(
                    "other-puuid", It.IsAny<CancellationToken>()))
                .ReturnsAsync(target);

            context.ViewModel.SummonerCommand.Execute(new Player
            {
                Puuid = "other-puuid"
            });
            await WaitUntilAsync(() => context.RegionManager.Invocations.Any(
                invocation => invocation.Method.Name ==
                    nameof(IRegionManager.RequestNavigate)));

            Assert.False(context.ViewModel.IsPreviewOpen);
            context.RegionManager.Verify(manager => manager.RequestNavigate(
                    RegionNames.SearchContent,
                    RegionNames.SummonerDetailView,
                    It.Is<NavigationParameters>(parameters =>
                        ReferenceEquals(parameters[ParameterNames.Summoner], target))),
                Times.Once);
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

        private static List<MatchModel> CreatePreviewMatches()
        {
            var results = new[]
            {
                true, true, false, true, true, false, false, true, true, true,
                false, true, false, true, true, false, true, false, true, false
            };
            return results.Select((win, index) => new MatchModel
            {
                GameId = index + 1,
                Participants =
                [
                    new Participant
                    {
                        ChampionId = 22,
                        Stats = new MatchStats
                        {
                            Win = win,
                            Kills = 5,
                            Deaths = 3,
                            Assists = 7
                        }
                    }
                ]
            }).ToList();
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

        private static async Task WaitForPreviewAsync(MatchHistoryViewModel viewModel)
        {
            await WaitUntilAsync(() => viewModel.IsPreviewOpen &&
                !viewModel.IsPreviewLoading && viewModel.Preview is not null);
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

            Assert.True(predicate(), "The expected view-model state was not reached in time.");
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
                GameResourceManager.Setup(service => service.GetProfileIconByIdAsync(
                        It.IsAny<int>()))
                    .ReturnsAsync((int profileIconId) => $"profile-{profileIconId}.png");
                ViewModel = new MatchHistoryViewModel(
                    RegionManager.Object,
                    GameService.Object,
                    GameResourceManager.Object,
                    SummonerService.Object,
                    EventAggregator);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public Mock<IGameService> GameService { get; } = new();

            public Mock<IGameResourceManager> GameResourceManager { get; } = new();

            public Mock<ISummonerService> SummonerService { get; } = new();

            public EventAggregator EventAggregator { get; } = new();

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

            public async Task NavigateAsync(string puuid,
                MatchModel selectedMatch = null,
                string hostRegionName = RegionNames.SummonerContent)
            {
                var parameters = new NavigationParameters
                {
                    { ParameterNames.Summoner, new SummonerAccount { Puuid = puuid } },
                    { ParameterNames.CanEdit, false },
                    { ParameterNames.HostRegionName, hostRegionName }
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
