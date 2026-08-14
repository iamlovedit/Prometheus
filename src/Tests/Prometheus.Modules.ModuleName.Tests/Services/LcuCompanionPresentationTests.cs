using Prometheus.Core.Models;
using Prometheus.Desktop.Services;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LcuCompanionPresentationTests
    {
        [Theory]
        [InlineData(GameQueueIds.RankedSoloDuo, LcuCompanionMode.RankedSoloDuo)]
        [InlineData(GameQueueIds.RankedFlex, LcuCompanionMode.RankedFlex)]
        [InlineData(GameQueueIds.Aram, LcuCompanionMode.Aram)]
        [InlineData(GameQueueIds.HextechAram, LcuCompanionMode.HextechAram)]
        [InlineData(GameQueueIds.HextechAramGameflow, LcuCompanionMode.HextechAram)]
        [InlineData(400, LcuCompanionMode.Matchmade)]
        public void GetMode_MapsSupportedQueues(int queueId, LcuCompanionMode expected)
        {
            var snapshot = CreateSnapshot(queueId);

            Assert.Equal(expected, LcuCompanionPresentation.GetMode(snapshot));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(GameQueueIds.Aram)]
        public void GetMode_WhenLobbyReportsHextechAram_PrefersExplicitHextechQueue(
            int gameflowQueueId)
        {
            var snapshot = CreateSnapshot(gameflowQueueId);
            snapshot.GameflowSession.GameData.GameMode = "ARAM";
            snapshot.GameflowSession.GameData.MapId = 12;
            snapshot.Lobby = new LobbySnapshot
            {
                GameConfig = new LobbyGameConfiguration
                {
                    QueueId = GameQueueIds.HextechAram,
                    GameMode = "ARAM",
                    MapId = 12
                }
            };

            Assert.Equal(
                GameQueueIds.HextechAram,
                LcuCompanionPresentation.GetQueueId(snapshot));
            Assert.Equal(
                LcuCompanionMode.HextechAram,
                LcuCompanionPresentation.GetMode(snapshot));
        }

        [Fact]
        public void GetMode_WhenGameflowUsesKiwiQueue_MapsToHextechAram()
        {
            var snapshot = CreateSnapshot(GameQueueIds.HextechAramGameflow);
            snapshot.GameflowSession.GameData.GameMode = "KIWI";
            snapshot.GameflowSession.GameData.MapId = 12;

            Assert.Equal(
                GameQueueIds.HextechAramGameflow,
                LcuCompanionPresentation.GetQueueId(snapshot));
            Assert.Equal(
                LcuCompanionMode.HextechAram,
                LcuCompanionPresentation.GetMode(snapshot));
        }

        [Fact]
        public void GetMode_WhenQueueIsUnknownButGameModeIsKiwi_MapsToHextechAram()
        {
            var snapshot = CreateSnapshot(9999);
            snapshot.GameflowSession.GameData.GameMode = "KIWI";
            snapshot.GameflowSession.GameData.MapId = 12;

            Assert.Equal(
                LcuCompanionMode.HextechAram,
                LcuCompanionPresentation.GetMode(snapshot));
        }

        [Fact]
        public void GetAramAutomationTarget_WhenQueueIsUnknownButGameModeIsKiwi_SelectsBenchChampion()
        {
            var snapshot = CreateSnapshot(9999, currentChampionId: 22);
            snapshot.GameflowSession.GameData.GameMode = "KIWI";
            snapshot.ChampionSelect.BenchEnabled = true;
            snapshot.ChampionSelect.BenchChampions =
            [
                new ChampionSelectBenchChampionSnapshot { ChampionId = 99 }
            ];

            var result = LcuCompanionPresentation.GetAramAutomationTarget(
                snapshot, [99]);

            Assert.Equal(99, result);
        }

        [Fact]
        public void GetAramAutomationTarget_WhenCurrentChampionIsPreferred_ReturnsCurrent()
        {
            var snapshot = CreateSnapshot(GameQueueIds.Aram, currentChampionId: 22);
            snapshot.ChampionSelect.BenchEnabled = true;
            snapshot.ChampionSelect.BenchChampions =
            [
                new ChampionSelectBenchChampionSnapshot { ChampionId = 99 }
            ];

            var result = LcuCompanionPresentation.GetAramAutomationTarget(
                snapshot, [22, 99]);

            Assert.Equal(22, result);
        }

        [Fact]
        public void GetAramAutomationTarget_SelectsHighestPriorityBenchChampion()
        {
            var snapshot = CreateSnapshot(GameQueueIds.Aram, currentChampionId: 22);
            snapshot.ChampionSelect.BenchEnabled = true;
            snapshot.ChampionSelect.BenchChampions =
            [
                new ChampionSelectBenchChampionSnapshot { ChampionId = 99 },
                new ChampionSelectBenchChampionSnapshot { ChampionId = 55 }
            ];

            var result = LcuCompanionPresentation.GetAramAutomationTarget(
                snapshot, [55, 99]);

            Assert.Equal(55, result);
        }

        [Fact]
        public void GetChampionSelectAutomationTarget_PrefersActualLocalAction()
        {
            var snapshot = CreateSnapshot(GameQueueIds.RankedSoloDuo);
            snapshot.ChampionSelect.Actions =
            [
                [
                    new ChampionSelectActionSnapshot
                    {
                        Id = 1,
                        ActorCellId = 1,
                        ChampionId = 99,
                        IsInProgress = true,
                        Type = "pick"
                    }
                ]
            ];

            var result = LcuCompanionPresentation.GetChampionSelectAutomationTarget(
                snapshot, "pick", [22, 55]);

            Assert.Equal(99, result);
        }

        [Fact]
        public void GetChampionSelectAutomationTarget_WithoutAction_UsesFirstPreference()
        {
            var snapshot = CreateSnapshot(GameQueueIds.RankedSoloDuo);

            var result = LcuCompanionPresentation.GetChampionSelectAutomationTarget(
                snapshot, "ban", [55, 99]);

            Assert.Equal(55, result);
        }

        [Fact]
        public void GetLocalChampionId_WhenChampionIsNotLocked_UsesPickIntent()
        {
            var snapshot = CreateSnapshot(GameQueueIds.RankedSoloDuo);
            snapshot.ChampionSelect.MyTeam[0].ChampionPickIntent = 103;

            Assert.Equal(103, LcuCompanionPresentation.GetLocalChampionId(snapshot));
        }

        [Theory]
        [InlineData("middle", "mid")]
        [InlineData("utility", "support")]
        [InlineData("bottom", "bottom")]
        public void GetLocalAssignedPosition_NormalizesLcuPosition(
            string assignedPosition,
            string expected)
        {
            var snapshot = CreateSnapshot(GameQueueIds.RankedSoloDuo);
            snapshot.ChampionSelect.MyTeam[0].AssignedPosition = assignedPosition;

            Assert.Equal(expected,
                LcuCompanionPresentation.GetLocalAssignedPosition(snapshot));
        }

        private static LiveMatchSnapshot CreateSnapshot(
            int queueId,
            int currentChampionId = 0)
        {
            return new LiveMatchSnapshot
            {
                GameflowPhase = GameflowPhase.ChampSelect,
                GameflowSession = new GameflowSessionSnapshot
                {
                    GameData = new GameflowGameData
                    {
                        QueueId = queueId,
                        GameMode = queueId is GameQueueIds.Aram or GameQueueIds.HextechAram
                            ? "ARAM"
                            : "CLASSIC",
                        MapId = queueId is GameQueueIds.Aram or GameQueueIds.HextechAram
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
                            ChampionId = currentChampionId
                        }
                    ]
                }
            };
        }
    }
}
