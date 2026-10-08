using Moq;
using Prism.Events;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.ViewModels;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public sealed class LcuCompanionRuneLifecycleTests
    {
        [Fact]
        public async Task Apply_WhenSnapshotChanges_ClearsApplyingStateAfterOwnerCompletes()
        {
            var fixture = CreateFixture();
            var applyCompletion = new TaskCompletionSource<RunePageApplyResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string _, RuneRecommendationOption _, CancellationToken _) =>
                    applyCompletion.Task);

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            Assert.True(fixture.ViewModel.IsApplyingRune);

            fixture.MatchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(CreateSnapshot(104)));
            Assert.True(fixture.ViewModel.IsApplyingRune);
            Assert.Equal("Applying rune page", fixture.ViewModel.RuneStatusText);
            Assert.Equal("Applying...", fixture.ViewModel.RuneApplyButtonText);

            applyCompletion.SetResult(new RunePageApplyResult
            {
                Status = RunePageApplyStatus.Applied,
                RunePageId = 1
            });
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "Ready to apply" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client" &&
                fixture.ViewModel.RuneChampionText == "Akali · Mid");
            Assert.False(fixture.ViewModel.IsApplyingRune);
            fixture.ViewModel.Stop();
        }

        [Fact]
        public async Task Apply_WhenChampionChangesAwayAndBack_RejectsStaleApplyResult()
        {
            var fixture = CreateFixture();
            var firstApply = new TaskCompletionSource<RunePageApplyResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string _, RuneRecommendationOption _, CancellationToken _) =>
                    firstApply.Task);

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();

            fixture.MatchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(CreateSnapshot(104)));
            fixture.MatchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(CreateSnapshot(103)));
            await WaitUntilAsync(() => fixture.ViewModel.HasRuneRecommendation &&
                fixture.ViewModel.RuneChampionText.StartsWith("Ahri", StringComparison.Ordinal));

            firstApply.SetResult(new RunePageApplyResult
            {
                Status = RunePageApplyStatus.Applied,
                RunePageId = 1
            });
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "Ready to apply" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            Assert.Equal("Ready to apply", fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        [Fact]
        public async Task Apply_WhenCurrentPlanChanges_KeepsApplyingPresentationAndRefreshesCurrentPlan()
        {
            var fixture = CreateFixture();
            var applyCompletion = new TaskCompletionSource<RunePageApplyResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string _, RuneRecommendationOption _, CancellationToken _) =>
                    applyCompletion.Task);

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            fixture.ViewModel.SelectWinRateRuneCommand.Execute();
            Assert.Equal("Applying rune page", fixture.ViewModel.RuneStatusText);
            Assert.Equal("Applying...", fixture.ViewModel.RuneApplyButtonText);

            applyCompletion.SetResult(new RunePageApplyResult
            {
                Status = RunePageApplyStatus.Applied,
                RunePageId = 1
            });
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "Ready to apply" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            Assert.Equal("Ready to apply", fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        [Theory]
        [InlineData(RunePageApplyStatus.ClientUnavailable, "League Client is unavailable")]
        [InlineData(RunePageApplyStatus.ConfirmationFailed, "Unable to confirm the active rune page")]
        public async Task Apply_WhenCurrentRequestFails_PreservesTerminalStatus(
            RunePageApplyStatus status,
            string expectedStatus)
        {
            var fixture = CreateFixture();
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunePageApplyResult { Status = status });

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == expectedStatus &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            Assert.Equal(expectedStatus, fixture.ViewModel.RuneStatusText);
            Assert.True(fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.MatchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(CreateSnapshot(103)));
            Assert.Equal(expectedStatus, fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        [Fact]
        public async Task Apply_WhenCurrentRequestThrows_PreservesFailureStatus()
        {
            var fixture = CreateFixture();
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Unable to apply"));

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "Unable to apply rune page" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            Assert.Equal("Unable to apply rune page", fixture.ViewModel.RuneStatusText);
            Assert.True(fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.MatchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(CreateSnapshot(103)));
            Assert.Equal("Unable to apply rune page", fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        [Fact]
        public async Task Apply_WhenCurrentRequestIsCancelled_PreservesCancelledStatus()
        {
            var fixture = CreateFixture();
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "Rune application cancelled" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            Assert.Equal("Rune application cancelled", fixture.ViewModel.RuneStatusText);
            Assert.True(fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.MatchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(CreateSnapshot(103)));
            Assert.Equal("Rune application cancelled", fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        [Fact]
        public async Task Apply_WhenFailedPlanIsChanged_ShowsCurrentPlanStatus()
        {
            var fixture = CreateFixture();
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunePageApplyResult { Status = RunePageApplyStatus.ConfirmationFailed });

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "Unable to confirm the active rune page" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            fixture.ViewModel.SelectWinRateRuneCommand.Execute();
            Assert.Equal("Ready to apply", fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        [Fact]
        public async Task Apply_WhenOldPlanFailsAfterSelectionChanges_DoesNotOverrideCurrentPlan()
        {
            var fixture = CreateFixture();
            var applyCompletion = new TaskCompletionSource<RunePageApplyResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .Returns(applyCompletion.Task);

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            fixture.ViewModel.SelectWinRateRuneCommand.Execute();
            applyCompletion.SetResult(new RunePageApplyResult
            {
                Status = RunePageApplyStatus.ConfirmationFailed
            });
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "Ready to apply" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            Assert.True(fixture.ViewModel.IsWinRateRuneSelected);
            Assert.Equal("Ready to apply", fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        [Fact]
        public async Task Apply_WhenFailedRequestIsRetried_ReplacesTerminalStatus()
        {
            var fixture = CreateFixture();
            var retryCompletion = new TaskCompletionSource<RunePageApplyResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.GameService
                .SetupSequence(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunePageApplyResult { Status = RunePageApplyStatus.ClientUnavailable })
                .Returns(retryCompletion.Task);

            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "League Client is unavailable" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            fixture.ViewModel.ApplyRuneCommand.Execute();
            Assert.Equal("Applying rune page", fixture.ViewModel.RuneStatusText);
            retryCompletion.SetResult(new RunePageApplyResult { Status = RunePageApplyStatus.Applied });
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "Rune page applied" &&
                fixture.ViewModel.RuneApplyButtonText == "Applied");

            fixture.MatchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(CreateSnapshot(103)));
            Assert.Equal("Rune page applied", fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        [Fact]
        public async Task Apply_WhenLanguageChanges_LocalizesTerminalStatusAgain()
        {
            var fixture = CreateFixture();
            fixture.GameService
                .Setup(service => service.ApplyRuneRecommendationAsync(
                    It.IsAny<string>(),
                    It.IsAny<RuneRecommendationOption>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunePageApplyResult { Status = RunePageApplyStatus.ClientUnavailable });
            fixture.ViewModel.Start();
            await WaitUntilAsync(() => fixture.ViewModel.ApplyRuneCommand.CanExecute());
            fixture.ViewModel.ApplyRuneCommand.Execute();
            await WaitUntilAsync(() => !fixture.ViewModel.IsApplyingRune &&
                fixture.ViewModel.RuneStatusText == "League Client is unavailable" &&
                fixture.ViewModel.RuneApplyButtonText == "Apply to League Client");

            fixture.ResourceService
                .Setup(service => service.FindResource<string>("Companion.Runes.ClientUnavailable"))
                .Returns("英雄联盟客户端不可用");
            fixture.EventAggregator.GetEvent<LanguageSwitchedEvent>().Publish();

            Assert.Equal("英雄联盟客户端不可用", fixture.ViewModel.RuneStatusText);
            fixture.ViewModel.Stop();
        }

        private static Fixture CreateFixture()
        {
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(CreateSnapshot(103));
            var gameService = new Mock<IGameService>();
            gameService.Setup(service => service.GetRuneRecommendationsAsync(
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((int championId, string _, bool _, CancellationToken _) =>
                    CreateRecommendation(championId));
            var resources = new Mock<IGameResourceManager>();
            resources.Setup(service => service.GetPerksAsync())
                .ReturnsAsync(PerkIds
                    .Concat(PerkIds.Take(9).Select(id => id + 1))
                    .Distinct()
                    .Select(id => new Perk { Id = id, Name = $"Perk {id}" })
                    .ToList());
            resources.Setup(service => service.GetPerkIconByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => $"{id}.png");
            resources.Setup(service => service.GetChampionSummarysAsync())
                .ReturnsAsync(new List<ChampionSummary>
                {
                    new() { Id = 103, Name = "Ahri" },
                    new() { Id = 104, Name = "Akali" }
                });
            var settings = new Mock<IGameAutomationSettings>();
            settings.SetupGet(value => value.PreferredPickChampionIds).Returns(Array.Empty<int>());
            settings.SetupGet(value => value.PreferredBanChampionIds).Returns(Array.Empty<int>());
            settings.SetupGet(value => value.PreferredAramChampionIds).Returns(Array.Empty<int>());
            var resourceService = new Mock<IResourceService>();
            resourceService.Setup(service => service.FindResource<string>(It.IsAny<string>()))
                .Returns((string _) => null);
            var eventAggregator = new EventAggregator();
            var viewModel = new LcuCompanionViewModel(
                eventAggregator,
                matchService.Object,
                gameService.Object,
                settings.Object,
                resources.Object,
                resourceService.Object);
            return new Fixture(viewModel, matchService, gameService, eventAggregator, resourceService);
        }

        private static LiveMatchSnapshot CreateSnapshot(int championId)
        {
            return new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.ChampSelect,
                GameflowSession = new GameflowSessionSnapshot
                {
                    GameData = new GameflowGameData
                    {
                        QueueId = GameQueueIds.RankedSoloDuo,
                        GameMode = "CLASSIC",
                        MapId = 11
                    }
                },
                ChampionSelect = new ChampionSelectSnapshot
                {
                    LocalPlayerCellId = 1,
                    MyTeam = new List<ChampionSelectTeamMemberSnapshot>
                    {
                        new ChampionSelectTeamMemberSnapshot
                        {
                            CellId = 1,
                            ChampionId = championId,
                            AssignedPosition = "middle"
                        }
                    }
                }
            };
        }

        private static RuneRecommendationSet CreateRecommendation(int championId)
        {
            var popular = new RuneRecommendationOption
            {
                PrimaryStyleId = 8100,
                SubStyleId = 8200,
                SelectedPerkIds = PerkIds,
                SampleCount = 1000,
                PickRateBasisPoints = 5000,
                WinRateBasisPoints = 5100
            };
            var winRate = new RuneRecommendationOption
            {
                PrimaryStyleId = 8000,
                SubStyleId = 8400,
                SelectedPerkIds = PerkIds.Take(9).Select(id => id + 1).ToArray(),
                SampleCount = 1000,
                PickRateBasisPoints = 4000,
                WinRateBasisPoints = 5500
            };
            return new RuneRecommendationSet
            {
                ChampionId = championId,
                Lane = "mid",
                Source = "QQ",
                Popular = popular,
                WinRate = winRate
            };
        }

        private static readonly int[] PerkIds =
        [8112, 8139, 8140, 8106, 8210, 8226, 5005, 5008, 5001];

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("Condition was not met in time.");
                }

                await Task.Delay(10);
            }
        }

        private sealed record Fixture(
            LcuCompanionViewModel ViewModel,
            Mock<IMatchService> MatchService,
            Mock<IGameService> GameService,
            EventAggregator EventAggregator,
            Mock<IResourceService> ResourceService);
    }
}
