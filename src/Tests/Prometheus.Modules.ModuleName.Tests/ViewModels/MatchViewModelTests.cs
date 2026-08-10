using Moq;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Modules.Match.ViewModels;
using Prometheus.Services.Interfaces.Client;
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
        public void LoadedPlayer_ProjectsTwentyPerMatchKdaItemsAndSupportsSelection()
        {
            using var context = new TestContext(CreateProgressSnapshot(version: 3));

            Assert.True(context.ViewModel.HasSelectedPlayer);
            Assert.Same(context.ViewModel.MyTeam[0], context.ViewModel.SelectedPlayer);
            Assert.True(context.ViewModel.SelectedPlayer.IsSelected);
            Assert.Equal(20, context.ViewModel.SelectedPlayer.RecentMatches.Count);

            var latest = context.ViewModel.SelectedPlayer.RecentMatches[0];
            Assert.Equal("#1", latest.IndexText);
            Assert.Equal("Win", latest.ResultText);
            Assert.Equal(10, latest.Kills);
            Assert.Equal(2, latest.Deaths);
            Assert.Equal(8, latest.Assists);
            Assert.Equal("Ranked Solo/Duo", latest.GameModeText);

            var nextPlayer = context.ViewModel.MyTeam[1];
            context.ViewModel.SelectPlayerCommand.Execute(nextPlayer);

            Assert.Same(nextPlayer, context.ViewModel.SelectedPlayer);
            Assert.True(nextPlayer.IsSelected);
            Assert.False(context.ViewModel.MyTeam[0].IsSelected);
        }

        [Fact]
        public void LoadedPlayer_ProjectsEveryResultStripSegmentWithMatchedTooltip()
        {
            using var context = new TestContext(CreateProgressSnapshot(version: 3));
            var player = context.ViewModel.MyTeam[0];

            // The strip must carry all 20 segments; the card layout can no longer clip them.
            Assert.Equal(20, player.RecentResults.Count);
            Assert.Equal(
                player.RecentMatches.Select(match => match.IsWin),
                player.RecentResults.Select(result => result.IsWin));

            // Segment i describes RecentMatches[i] (both are newest-first).
            Assert.Equal("Match 1 · Win · 10/2/8 · Ranked Solo/Duo",
                player.RecentResults[0].ResultTooltip);
            Assert.Equal("Match 2 · Loss · 11/3/9 · ARAM",
                player.RecentResults[1].ResultTooltip);
        }

        [Fact]
        public void Streak_IsCountedFromTheNewestResultForward()
        {
            var snapshot = CreateProgressSnapshot(version: 3);
            var target = snapshot.Roster.MyTeam[0];
            // Newest-first: three losses at the front, a win behind them.
            target.RecentResults = new[] { false, false, false, true, true }
                .Concat(Enumerable.Repeat(true, 15))
                .ToArray();

            using var context = new TestContext(snapshot);
            var player = context.ViewModel.MyTeam[0];

            Assert.True(player.HasStreak);
            Assert.False(player.StreakIsWinning);
            Assert.Equal(3, player.StreakCount);
            Assert.Equal("3 Loss Streak", player.StreakText);
        }

        [Fact]
        public void Streak_WhenShorterThanThreeGames_IsNotDisplayed()
        {
            var snapshot = CreateProgressSnapshot(version: 3);
            var target = snapshot.Roster.MyTeam[0];
            target.RecentResults = new[] { true, true, false, true, false }
                .Concat(Enumerable.Repeat(true, 15))
                .ToArray();

            using var context = new TestContext(snapshot);

            Assert.False(context.ViewModel.MyTeam[0].HasStreak);
        }

        [Fact]
        public void SelectPlayerCommand_WhenInvokedOnSelectedPlayer_CollapsesDetailBar()
        {
            using var context = new TestContext(CreateProgressSnapshot(version: 3));
            var player = context.ViewModel.MyTeam[0];
            Assert.Same(player, context.ViewModel.SelectedPlayer);

            context.ViewModel.SelectPlayerCommand.Execute(player);

            Assert.Null(context.ViewModel.SelectedPlayer);
            Assert.False(player.IsSelected);
            Assert.False(context.ViewModel.HasSelectedPlayer);
        }

        [Fact]
        public void CloseDetailsCommand_SurvivesSubsequentSnapshots()
        {
            using var context = new TestContext(CreateProgressSnapshot(version: 4));
            Assert.True(context.ViewModel.HasSelectedPlayer);

            context.ViewModel.CloseDetailsCommand.Execute();
            context.Publish(CreateProgressSnapshot(version: 5));

            // A refresh must not silently reopen a bar the user dismissed.
            Assert.Null(context.ViewModel.SelectedPlayer);
            Assert.False(context.ViewModel.HasSelectedPlayer);
        }

        [Fact]
        public void EmptyRoster_ClearsDismissalSoTheNextMatchAutoOpens()
        {
            using var context = new TestContext(CreateProgressSnapshot(version: 4));
            context.ViewModel.CloseDetailsCommand.Execute();

            context.Publish(new LiveMatchSnapshot { Version = 5 });
            context.Publish(CreateProgressSnapshot(version: 6));

            Assert.True(context.ViewModel.HasSelectedPlayer);
        }

        [Fact]
        public void SnapshotRefresh_PreservesSelectedPlayerByPublicIdentity()
        {
            using var context = new TestContext(CreateProgressSnapshot(version: 4));
            context.ViewModel.SelectPlayerCommand.Execute(context.ViewModel.MyTeam[2]);

            context.Publish(CreateProgressSnapshot(version: 5));

            Assert.Equal("puuid-Ally2", context.ViewModel.SelectedPlayer.Puuid);
            Assert.Same(context.ViewModel.MyTeam[2], context.ViewModel.SelectedPlayer);
            Assert.True(context.ViewModel.SelectedPlayer.IsSelected);
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
        public void PlayAgain_IsVisibleDuringPostGameAndEnablesWhenClientBecomesIdle()
        {
            using var context = new TestContext(CreatePostGameSnapshot(
                version: 10,
                queueId: GameQueueIds.RankedSoloDuo));

            Assert.True(context.ViewModel.IsPlayAgainVisible);
            Assert.False(context.ViewModel.PlayAgainCommand.CanExecute());
            Assert.Equal(
                "Waiting for League Client to finish post-game processing",
                context.ViewModel.PlayAgainToolTip);

            context.Publish(new LiveMatchSnapshot
            {
                Version = 11,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None
            });

            Assert.True(context.ViewModel.IsPlayAgainVisible);
            Assert.True(context.ViewModel.PlayAgainCommand.CanExecute());
            Assert.Equal("Create a Ranked Solo/Duo lobby",
                context.ViewModel.PlayAgainToolTip);
        }

        [Fact]
        public void PostGameSnapshot_ProjectsSettlementSummaryInsteadOfEmptyRoster()
        {
            using var context = new TestContext(CreatePostGameSnapshot(
                version: 12,
                queueId: GameQueueIds.RankedSoloDuo));

            Assert.True(context.ViewModel.IsPostGameVisible);
            Assert.False(context.ViewModel.IsPostGameLoading);
            Assert.False(context.ViewModel.ShowEmptyState);
            Assert.Equal("Match result", context.ViewModel.PageTitle);
            Assert.Equal("Victory", context.ViewModel.PostGameResultText);
            Assert.Equal("12", context.ViewModel.PostGameKills);
            Assert.Equal("4", context.ViewModel.PostGameDeaths);
            Assert.Equal("9", context.ViewModel.PostGameAssists);
            Assert.Equal("CLASSIC", context.ViewModel.PostGameModeText);
            Assert.Equal("25:07", context.ViewModel.PostGameDurationText);
            Assert.Equal("Game 987654", context.ViewModel.PostGameGameIdText);
            Assert.Equal("Report ready", context.ViewModel.DataStatusText);
        }

        [Fact]
        public void PostGamePhase_WhenReportIsNotReady_ShowsGeneratingState()
        {
            using var context = new TestContext(new LiveMatchSnapshot
            {
                Version = 13,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.EndOfGame,
                DataQuality = DataQuality.Partial
            });

            Assert.True(context.ViewModel.IsPostGameVisible);
            Assert.True(context.ViewModel.IsPostGameLoading);
            Assert.False(context.ViewModel.ShowEmptyState);
            Assert.Equal("Generating match report",
                context.ViewModel.PostGameResultText);
            Assert.Equal("Generating report", context.ViewModel.DataStatusText);
        }

        [Fact]
        public void PostGameSnapshot_ProjectsDoughnutsSummaryAndTenPlayerDetails()
        {
            using var context = new TestContext(CreatePostGameSnapshot(
                version: 14,
                queueId: GameQueueIds.RankedSoloDuo));

            Assert.True(context.ViewModel.HasPostGameTeamDetails);
            Assert.Equal(5, context.ViewModel.PostGameMyTeamSlices.Count);
            Assert.Equal(5, context.ViewModel.PostGameTheirTeamSlices.Count);
            Assert.Equal(10, context.ViewModel.PostGamePlayers.Count);
            Assert.Equal("68%", context.ViewModel.PostGameKillParticipationText);
            Assert.Equal("24%", context.ViewModel.PostGameDamageShareText);
            Assert.Equal("24%", context.ViewModel.PostGameGoldShareText);
            Assert.Equal("Champion damage", context.ViewModel.PostGameMetricLabel);
            Assert.Equal("118k", context.ViewModel.PostGameMyTeamTotalText);
            Assert.Equal("104k", context.ViewModel.PostGameTheirTeamTotalText);
            Assert.Equal("You", context.ViewModel.PostGameMyTeamSlices[1].DisplayName);
            Assert.Equal("24%", context.ViewModel.PostGameMyTeamSlices[1].PercentageText);
            Assert.True(context.ViewModel.PostGamePlayers[1].IsLocalPlayer);

            context.ViewModel.SelectPostGameMetricCommand.Execute("GoldEarned");

            Assert.True(context.ViewModel.IsPostGameGoldMetric);
            Assert.False(context.ViewModel.IsPostGameDamageMetric);
            Assert.Equal("Gold earned", context.ViewModel.PostGameMetricLabel);
            Assert.Equal("64.2k", context.ViewModel.PostGameMyTeamTotalText);
            Assert.Equal("59.8k", context.ViewModel.PostGameTheirTeamTotalText);
            Assert.Equal(15400, context.ViewModel.PostGameMyTeamSlices[1].Value);

            context.ViewModel.SelectPostGameMetricCommand.Execute("DamageTaken");

            Assert.True(context.ViewModel.IsPostGameDamageTakenMetric);
            Assert.Equal("132.5k", context.ViewModel.PostGameMyTeamTotalText);
            Assert.Equal("147.2k", context.ViewModel.PostGameTheirTeamTotalText);
        }

        [Fact]
        public void PostGameSnapshot_WithoutTeamPlayers_KeepsLocalSummaryAndHidesCharts()
        {
            var snapshot = CreatePostGameSnapshot(
                version: 15,
                queueId: GameQueueIds.RankedSoloDuo);
            snapshot.PostGame.Teams = [];

            using var context = new TestContext(snapshot);

            Assert.True(context.ViewModel.IsPostGameVisible);
            Assert.False(context.ViewModel.HasPostGameTeamDetails);
            Assert.Empty(context.ViewModel.PostGameMyTeamSlices);
            Assert.Empty(context.ViewModel.PostGameTheirTeamSlices);
            Assert.Empty(context.ViewModel.PostGamePlayers);
            Assert.Equal("12 / 4 / 9 · KDA 5.3",
                context.ViewModel.PostGameLocalKdaText);
        }

        [Theory]
        [InlineData(GameQueueIds.RankedSoloDuo)]
        [InlineData(GameQueueIds.RankedFlex)]
        [InlineData(GameQueueIds.Aram)]
        [InlineData(GameQueueIds.HextechAram)]
        public async Task PlayAgain_UsesCompletedMatchQueueWithoutChangingSettings(
            int queueId)
        {
            using var context = new TestContext(CreatePostGameSnapshot(
                version: 20,
                queueId: queueId));
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
                    QueueId = queueId
                });
            context.Publish(new LiveMatchSnapshot
            {
                Version = 21,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None
            });

            context.ViewModel.PlayAgainCommand.Execute();

            Assert.Equal(queueId,
                await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            context.GameService.Verify(service => service.CreateMatchmadeLobbyAsync(
                queueId,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PlayAgain_WhenPostGameOmitsQueue_UsesActiveMatchQueue()
        {
            var activeMatch = CreateProgressSnapshot(version: 30);
            activeMatch.GameflowSession = new GameflowSessionSnapshot
            {
                GameData = new GameflowGameData
                {
                    QueueId = GameQueueIds.Aram
                }
            };
            using var context = new TestContext(activeMatch);
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

            context.Publish(CreatePostGameSnapshot(version: 31, queueId: 0));
            context.Publish(new LiveMatchSnapshot
            {
                Version = 32,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None
            });
            context.ViewModel.PlayAgainCommand.Execute();

            Assert.Equal(GameQueueIds.Aram,
                await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void PlayAgain_UnsupportedQueueRemainsVisibleButDisabled()
        {
            using var context = new TestContext(CreatePostGameSnapshot(
                version: 40,
                queueId: GameQueueIds.NormalDraft));

            context.Publish(new LiveMatchSnapshot
            {
                Version = 41,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None
            });

            Assert.True(context.ViewModel.IsPlayAgainVisible);
            Assert.False(context.ViewModel.PlayAgainCommand.CanExecute());
            Assert.Equal("Play again is not supported for this mode",
                context.ViewModel.PlayAgainToolTip);
            context.GameService.Verify(service => service.CreateMatchmadeLobbyAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void PlayAgain_NewLobbyClearsCompletedMatchContext()
        {
            using var context = new TestContext(CreatePostGameSnapshot(
                version: 50,
                queueId: GameQueueIds.RankedFlex));

            context.Publish(new LiveMatchSnapshot
            {
                Version = 51,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.Lobby
            });

            Assert.False(context.ViewModel.IsPlayAgainVisible);
            Assert.False(context.ViewModel.PlayAgainCommand.CanExecute());
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

        private static LiveMatchSnapshot CreatePostGameSnapshot(long version, int queueId)
        {
            var localPlayer = CreatePostGamePlayer(
                "Local", 22, 12, 4, 9, 15400, 206, 28600, 18200, 7,
                isLocalPlayer: true, won: true);
            var myTeam = new List<PostGamePlayerSnapshot>
            {
                CreatePostGamePlayer("AllyMid", 1, 10, 3, 12, 16100, 224,
                    31800, 19700, 8, won: true),
                localPlayer,
                CreatePostGamePlayer("AllyJungle", 2, 4, 5, 15, 13200, 151,
                    22100, 31600, 6, won: true),
                CreatePostGamePlayer("AllyTop", 3, 3, 6, 10, 12100, 132,
                    19700, 38900, 5, won: true),
                CreatePostGamePlayer("AllySupport", 4, 2, 6, 18, 7400, 29,
                    15800, 24100, 12, won: true)
            };
            var theirTeam = new List<PostGamePlayerSnapshot>
            {
                CreatePostGamePlayer("EnemyMid", 5, 9, 6, 8, 14800, 215,
                    27100, 22900, 6),
                CreatePostGamePlayer("EnemyBottom", 6, 8, 7, 7, 14300, 198,
                    24500, 20100, 5),
                CreatePostGamePlayer("EnemyJungle", 7, 3, 6, 12, 12800, 144,
                    21000, 35400, 7),
                CreatePostGamePlayer("EnemyTop", 8, 3, 6, 9, 11600, 119,
                    17900, 42100, 4),
                CreatePostGamePlayer("EnemySupport", 9, 1, 6, 13, 6300, 25,
                    13500, 26700, 11)
            };
            return new LiveMatchSnapshot
            {
                Version = version,
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.EndOfGame,
                DataQuality = DataQuality.Complete,
                UpdatedAt = DateTimeOffset.UtcNow,
                PostGame = new PostGameSnapshot
                {
                    GameId = 987654,
                    GameLength = 1507,
                    QueueId = queueId,
                    GameMode = "CLASSIC",
                    LocalPlayer = localPlayer,
                    Teams =
                    [
                        new PostGameTeamSnapshot
                        {
                            Team = "TEAM_ONE",
                            Won = true,
                            Players = myTeam
                        },
                        new PostGameTeamSnapshot
                        {
                            Team = "TEAM_TWO",
                            Won = false,
                            Players = theirTeam
                        }
                    ]
                }
            };
        }

        private static PostGamePlayerSnapshot CreatePostGamePlayer(
            string name,
            int championId,
            int kills,
            int deaths,
            int assists,
            int gold,
            int creepScore,
            int championDamage,
            int damageTaken,
            int visionScore,
            bool isLocalPlayer = false,
            bool won = false)
        {
            return new PostGamePlayerSnapshot
            {
                ChampionId = championId,
                ChampionName = $"Champion{championId}",
                ChampionIcon = $"champion-{championId}",
                SummonerName = name,
                Puuid = $"{name.ToLowerInvariant()}-puuid",
                SummonerId = championId + 1000,
                IsLocalPlayer = isLocalPlayer,
                Stats = new PostGamePlayerStatsSnapshot
                {
                    ChampionsKilled = kills,
                    Deaths = deaths,
                    Assists = assists,
                    GoldEarned = gold,
                    MinionsKilled = creepScore,
                    TotalDamageDealtToChampions = championDamage,
                    TotalDamageTaken = damageTaken,
                    VisionScore = visionScore,
                    Win = won ? 1 : 0
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
                RecentResults = Enumerable.Range(0, 20)
                    .Select(index => index % 3 != 1)
                    .ToArray(),
                RecentMatches = Enumerable.Range(0, 20)
                    .Select(index => new LiveMatchRecentMatchSnapshot
                    {
                        GameId = 1000 + index,
                        ChampionId = 10 + index,
                        ChampionIcon = $"champion-{10 + index}",
                        IsWin = index % 3 != 1,
                        Kills = 10 + index,
                        Deaths = 2 + index,
                        Assists = 8 + index,
                        GameMode = index % 2 == 0 ? "Ranked Solo/Duo" : "ARAM"
                    })
                    .ToArray(),
                IsLocalPlayer = slot == 0
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
                GameService.Setup(service => service.CreateMatchmadeLobbyAsync(
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((int queueId, CancellationToken _) =>
                        new MatchmadeLobbyCreationResult
                        {
                            Status = MatchmadeLobbyCreationStatus.Created,
                            QueueId = queueId
                        });
                ViewModel = new MatchViewModel(
                    RegionManager.Object,
                    EventAggregator,
                    MatchService.Object,
                    GameService.Object,
                    ResourceService.Object);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public EventAggregator EventAggregator { get; }

            public Mock<IMatchService> MatchService { get; } = new();

            public Mock<IGameService> GameService { get; } = new();

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
