using Prometheus.Core.Models;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class GameModeResolverTests
    {
        [Theory]
        [InlineData(GameQueueIds.HextechAram, "ARAM", 12, GameModeKind.HextechAram)]
        [InlineData(GameQueueIds.HextechAramGameflow, "KIWI", 12, GameModeKind.HextechAram)]
        [InlineData(9999, "KIWI", 0, GameModeKind.HextechAram)]
        [InlineData(GameQueueIds.Aram, "ARAM", 12, GameModeKind.Aram)]
        [InlineData(9999, "ARAM", 12, GameModeKind.Aram)]
        [InlineData(GameQueueIds.RankedSoloDuo, "CLASSIC", 11, GameModeKind.RankedSoloDuo)]
        [InlineData(GameQueueIds.RankedFlex, "CLASSIC", 11, GameModeKind.RankedFlex)]
        [InlineData(400, "CLASSIC", 11, GameModeKind.Matchmade)]
        [InlineData(0, "", 0, GameModeKind.Unknown)]
        public void Classify_UsesQueueModeAndMapWithHextechPriority(
            int queueId,
            string gameMode,
            int mapId,
            GameModeKind expected)
        {
            Assert.Equal(expected,
                GameModeResolver.Classify(queueId, gameMode, mapId));
        }

        [Theory]
        [InlineData(GameQueueIds.RankedSoloDuo, true)]
        [InlineData(GameQueueIds.RankedFlex, true)]
        [InlineData(GameQueueIds.Aram, true)]
        [InlineData(GameQueueIds.HextechAram, true)]
        [InlineData(GameQueueIds.HextechAramGameflow, false)]
        [InlineData(9999, false)]
        public void IsQuickMatchQueue_OnlyAllowsPublicCreationQueues(
            int queueId,
            bool expected)
        {
            Assert.Equal(expected, GameModeResolver.IsQuickMatchQueue(queueId));
        }

        [Fact]
        public void Classify_LiveSnapshot_PrefersHextechSignalFromAnySource()
        {
            var snapshot = CreateSnapshot(
                gameflowQueueId: GameQueueIds.Aram,
                gameMode: "ARAM",
                lobbyQueueId: GameQueueIds.HextechAram);

            Assert.Equal(GameModeKind.HextechAram,
                GameModeResolver.Classify(snapshot));
            Assert.Equal(GameQueueIds.HextechAram,
                GameModeResolver.ResolveQueueId(snapshot));
        }

        [Fact]
        public void Classify_LiveSnapshot_UsesLaterSourceWhenGameflowQueueIsTransitional()
        {
            var snapshot = CreateSnapshot(
                gameflowQueueId: 0,
                gameMode: "ARAM",
                lobbyQueueId: GameQueueIds.RankedSoloDuo);

            Assert.Equal(GameModeKind.RankedSoloDuo,
                GameModeResolver.Classify(snapshot));
            Assert.False(GameModeResolver.IsAram(snapshot));
            Assert.Equal(GameQueueIds.RankedSoloDuo,
                GameModeResolver.ResolveQueueId(snapshot));
        }

        [Fact]
        public void Classify_LiveSnapshot_UsesMatchmakingQueueWhenEarlierSourcesAreEmpty()
        {
            var snapshot = CreateSnapshot(
                gameflowQueueId: 0,
                gameMode: string.Empty,
                matchmakingQueueId: GameQueueIds.Aram);

            Assert.Equal(GameModeKind.Aram,
                GameModeResolver.Classify(snapshot));
            Assert.True(GameModeResolver.IsAram(snapshot));
            Assert.Equal(GameQueueIds.Aram,
                GameModeResolver.ResolveQueueId(snapshot));
        }

        private static LiveMatchSnapshot CreateSnapshot(
            int gameflowQueueId,
            string gameMode,
            int lobbyQueueId = 0,
            int matchmakingQueueId = 0)
        {
            return new LiveMatchSnapshot
            {
                GameflowSession = new GameflowSessionSnapshot
                {
                    GameData = new GameflowGameData
                    {
                        QueueId = gameflowQueueId,
                        GameMode = gameMode
                    }
                },
                Lobby = new LobbySnapshot
                {
                    GameConfig = new LobbyGameConfiguration
                    {
                        QueueId = lobbyQueueId
                    }
                },
                Matchmaking = new MatchmakingSnapshot
                {
                    Queue = new MatchmakingQueue
                    {
                        Id = matchmakingQueueId
                    }
                }
            };
        }
    }
}
