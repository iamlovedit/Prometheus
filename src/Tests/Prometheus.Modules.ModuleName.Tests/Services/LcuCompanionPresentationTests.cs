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
        [InlineData(400, LcuCompanionMode.Matchmade)]
        public void GetMode_MapsSupportedQueues(int queueId, LcuCompanionMode expected)
        {
            var snapshot = CreateSnapshot(queueId);

            Assert.Equal(expected, LcuCompanionPresentation.GetMode(snapshot));
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
