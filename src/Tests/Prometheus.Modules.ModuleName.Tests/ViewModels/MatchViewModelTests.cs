using Moq;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Modules.Match.ViewModels;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class MatchViewModelTests
    {
        [Fact]
        public void ChampionSelectSnapshot_WithFiveVersusFiveSkeleton_MapsEverySlot()
        {
            using var context = new TestContext(CreateChampionSelectSkeleton(version: 1));

            Assert.Equal(5, context.ViewModel.MyTeam.Count);
            Assert.Equal(5, context.ViewModel.TheirTeam.Count);
            Assert.True(context.ViewModel.HasRoster);
            Assert.False(context.ViewModel.ShowEmptyState);
            Assert.All(context.ViewModel.MyTeam, player =>
                Assert.Equal(LiveMatchPlayerDataState.Placeholder, player.DataState));
            Assert.All(context.ViewModel.TheirTeam, player =>
            {
                Assert.True(player.IsHidden);
                Assert.True(player.IsPlaceholder);
                Assert.False(player.CanOpenProfile);
                Assert.Equal(LiveMatchPlayerDataState.Hidden, player.DataState);
            });
        }

        [Fact]
        public void ProgressiveSnapshot_ReportsLoadedPlayersOutOfTen()
        {
            using var context = new TestContext(CreateProgressSnapshot(version: 2));

            Assert.Equal("Loading · 5/10", context.ViewModel.DataStatusText);
            Assert.Equal("Ready", context.ViewModel.MyTeamStatusText);
            Assert.Equal("Loading", context.ViewModel.TheirTeamStatusText);
            Assert.All(context.ViewModel.MyTeam, player =>
                Assert.Equal(LiveMatchPlayerDataState.Loaded, player.DataState));
            Assert.All(context.ViewModel.TheirTeam, player => Assert.True(player.IsLoading));
        }

        [Fact]
        public void SnapshotChanged_WhenOlderVersionArrivesLast_DoesNotRegressDisplay()
        {
            using var context = new TestContext(CreateNamedSnapshot(1, "Initial"));

            context.Publish(CreateNamedSnapshot(3, "Newest"));
            context.Publish(CreateNamedSnapshot(2, "Stale"), updateCurrent: false);

            Assert.Equal("Newest#TST", context.ViewModel.MyTeam[0].DisplayName);
        }

        [Fact]
        public void OpenPlayerCommand_WhenPlayerIsPublicAndIdentified_PublishesNavigationEvent()
        {
            var snapshot = CreateNamedSnapshot(4, "Navigable");
            using var context = new TestContext(snapshot);
            SummonerAccount publishedAccount = null;
            context.EventAggregator.GetEvent<SearchSummonerEvent>()
                .Subscribe(account => publishedAccount = account);

            var player = context.ViewModel.MyTeam[0];
            context.ViewModel.OpenPlayerCommand.Execute(player);

            Assert.True(player.CanOpenProfile);
            Assert.Same(snapshot.Roster.MyTeam[0].Summoner, publishedAccount);
        }

        [Fact]
        public void OpenPlayerCommand_WithPublicPuuidBeforeEnrichment_PublishesMinimalIdentity()
        {
            var snapshot = CreateNamedSnapshot(5, "StillLoading");
            snapshot.Roster.MyTeam[0].Summoner = null;
            snapshot.Roster.MyTeam[0].DataState = LiveMatchPlayerDataState.Loading;
            using var context = new TestContext(snapshot);
            SummonerAccount publishedAccount = null;
            context.EventAggregator.GetEvent<SearchSummonerEvent>()
                .Subscribe(account => publishedAccount = account);

            var player = context.ViewModel.MyTeam[0];
            context.ViewModel.OpenPlayerCommand.Execute(player);

            Assert.True(player.CanOpenProfile);
            Assert.Equal(snapshot.Roster.MyTeam[0].Puuid, publishedAccount?.Puuid);
        }

        [Fact]
        public void OpenPlayerCommand_WhenPlayerIsHidden_DoesNotPublishNavigationEvent()
        {
            using var context = new TestContext(CreateChampionSelectSkeleton(version: 6));
            var publishCount = 0;
            context.EventAggregator.GetEvent<SearchSummonerEvent>()
                .Subscribe(_ => publishCount++);

            context.ViewModel.OpenPlayerCommand.Execute(context.ViewModel.TheirTeam[0]);

            Assert.Equal(0, publishCount);
        }

        [Fact]
        public void UnavailablePlayer_UsesUnavailableCopyAndNormalizesLegacyJunglePosition()
        {
            var snapshot = CreateNamedSnapshot(7, "Unavailable");
            var source = snapshot.Roster.MyTeam[0];
            source.Position = "JUG";
            source.Puuid = string.Empty;
            source.Summoner = null;
            source.DataState = LiveMatchPlayerDataState.Unavailable;
            using var context = new TestContext(snapshot);

            var player = context.ViewModel.MyTeam[0];
            Assert.Equal("Jungle", player.PositionText);
            Assert.Equal("Player data unavailable", player.StatusText);
            Assert.False(player.CanOpenProfile);
        }

        [Fact]
        public void RefreshCommand_DelegatesRefreshToMatchService()
        {
            using var context = new TestContext(CreateChampionSelectSkeleton(version: 5));

            context.ViewModel.RefreshCommand.Execute();

            context.MatchService.Verify(service => service.RefreshAsync(
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void NavigationLifecycle_PausesSubscriptionAndCatchesUpFromCurrent()
        {
            using var context = new TestContext(CreateNamedSnapshot(6, "BeforeLeave"));
            context.ViewModel.OnNavigatedFrom(null);

            context.Publish(CreateNamedSnapshot(7, "WhileAway"));
            Assert.Equal("BeforeLeave#TST", context.ViewModel.MyTeam[0].DisplayName);

            context.ViewModel.OnNavigatedTo(null);
            Assert.Equal("WhileAway#TST", context.ViewModel.MyTeam[0].DisplayName);
        }

        private static LiveMatchSnapshot CreateChampionSelectSkeleton(long version)
        {
            return new LiveMatchSnapshot
            {
                Version = version,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.ChampSelect,
                DataQuality = DataQuality.Partial,
                UpdatedAt = DateTimeOffset.UtcNow,
                Roster = new LiveMatchRosterSnapshot
                {
                    GameId = 271828,
                    SourcePhase = GameflowPhase.ChampSelect,
                    Signature = $"skeleton:{version}",
                    IsResolving = true,
                    MyTeam = Enumerable.Range(0, 5)
                        .Select(slot => new LiveMatchPlayerSnapshot
                        {
                            Slot = slot,
                            CellId = slot,
                            IsPlaceholder = true,
                            DataState = LiveMatchPlayerDataState.Placeholder
                        })
                        .ToArray(),
                    TheirTeam = Enumerable.Range(0, 5)
                        .Select(slot => new LiveMatchPlayerSnapshot
                        {
                            Slot = slot,
                            CellId = slot + 5,
                            IsHidden = true,
                            IsPlaceholder = true,
                            Puuid = string.Empty,
                            DataState = LiveMatchPlayerDataState.Hidden
                        })
                        .ToArray()
                }
            };
        }

        private static LiveMatchSnapshot CreateProgressSnapshot(long version)
        {
            return new LiveMatchSnapshot
            {
                Version = version,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.InProgress,
                DataQuality = DataQuality.Partial,
                UpdatedAt = DateTimeOffset.UtcNow,
                Roster = new LiveMatchRosterSnapshot
                {
                    GameId = 161803,
                    SourcePhase = GameflowPhase.InProgress,
                    Signature = $"progress:{version}",
                    IsResolving = false,
                    MyTeam = Enumerable.Range(0, 5)
                        .Select(slot => CreateLoadedPlayer(slot, $"Ally{slot}"))
                        .ToArray(),
                    TheirTeam = Enumerable.Range(0, 5)
                        .Select(slot => new LiveMatchPlayerSnapshot
                        {
                            Slot = slot,
                            CellId = slot + 5,
                            Puuid = $"enemy-{slot}",
                            DisplayName = $"Enemy{slot}",
                            DataState = LiveMatchPlayerDataState.Loading
                        })
                        .ToArray()
                }
            };
        }

        private static LiveMatchSnapshot CreateNamedSnapshot(long version, string name)
        {
            var myTeam = new List<LiveMatchPlayerSnapshot>
            {
                CreateLoadedPlayer(0, name)
            };
            myTeam.AddRange(Enumerable.Range(1, 4).Select(slot =>
                new LiveMatchPlayerSnapshot
                {
                    Slot = slot,
                    CellId = slot,
                    IsPlaceholder = true,
                    DataState = LiveMatchPlayerDataState.Placeholder
                }));

            return new LiveMatchSnapshot
            {
                Version = version,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.InProgress,
                DataQuality = DataQuality.Partial,
                UpdatedAt = DateTimeOffset.UtcNow,
                Roster = new LiveMatchRosterSnapshot
                {
                    GameId = 314159,
                    SourcePhase = GameflowPhase.InProgress,
                    Signature = $"named:{version}",
                    MyTeam = myTeam,
                    TheirTeam = Enumerable.Range(0, 5)
                        .Select(slot => new LiveMatchPlayerSnapshot
                        {
                            Slot = slot,
                            CellId = slot + 5,
                            IsPlaceholder = true,
                            DataState = LiveMatchPlayerDataState.Placeholder
                        })
                        .ToArray()
                }
            };
        }

        private static LiveMatchPlayerSnapshot CreateLoadedPlayer(int slot, string name)
        {
            var puuid = $"puuid-{name}";
            return new LiveMatchPlayerSnapshot
            {
                Slot = slot,
                CellId = slot,
                Puuid = puuid,
                DisplayName = name,
                Position = slot switch
                {
                    0 => "TOP",
                    1 => "JUNGLE",
                    2 => "MIDDLE",
                    3 => "BOTTOM",
                    _ => "UTILITY"
                },
                DataState = LiveMatchPlayerDataState.Loaded,
                Summoner = new SummonerAccount
                {
                    GameName = name,
                    TagLine = "TST",
                    Puuid = puuid
                },
                SoloRank = new Rank
                {
                    Tier = Tier.EMERALD,
                    Division = "II",
                    LeaguePoints = 55
                },
                RecentWins = 12,
                RecentLosses = 8,
                RecentMatchCount = 20,
                AverageKda = 3.5,
                RecentResults = [true, false, true, true, false]
            };
        }

        private sealed class TestContext : IDisposable
        {
            private LiveMatchSnapshot _current;

            public TestContext(LiveMatchSnapshot snapshot)
            {
                _current = snapshot;
                EventAggregator = new EventAggregator();
                MatchService.SetupGet(service => service.Current)
                    .Returns(() => _current);
                MatchService.Setup(service => service.RefreshAsync(
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                ViewModel = new MatchViewModel(
                    RegionManager.Object,
                    EventAggregator,
                    MatchService.Object,
                    ResourceService.Object);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public EventAggregator EventAggregator { get; }

            public Mock<IMatchService> MatchService { get; } = new();

            public Mock<IResourceService> ResourceService { get; } = new();

            public MatchViewModel ViewModel { get; }

            public void Publish(LiveMatchSnapshot snapshot, bool updateCurrent = true)
            {
                if (updateCurrent)
                {
                    _current = snapshot;
                }

                MatchService.Raise(service => service.SnapshotChanged += null,
                    new LiveMatchSnapshotChangedEventArgs(snapshot));
            }

            public void Dispose()
            {
                ViewModel.Destroy();
            }
        }
    }
}
