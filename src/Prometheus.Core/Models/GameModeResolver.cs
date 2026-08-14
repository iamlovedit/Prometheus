namespace Prometheus.Core.Models
{
    public enum GameModeKind
    {
        Unknown,
        Matchmade,
        RankedSoloDuo,
        RankedFlex,
        Aram,
        HextechAram
    }

    /// <summary>
    /// Centralizes queue/mode classification shared by live-match, history and
    /// presentation code. The resolver deliberately does not provide localized
    /// display text. Quick-match eligibility is kept here as a queue capability
    /// so internal recognition-only queues cannot accidentally be persisted or
    /// sent to the lobby creation endpoint.
    /// </summary>
    public static class GameModeResolver
    {
        public static GameModeKind Classify(
            int queueId,
            string gameMode = null,
            int mapId = 0)
        {
            if (IsHextechAram(queueId, gameMode))
            {
                return GameModeKind.HextechAram;
            }

            return queueId switch
            {
                GameQueueIds.RankedSoloDuo => GameModeKind.RankedSoloDuo,
                GameQueueIds.RankedFlex => GameModeKind.RankedFlex,
                GameQueueIds.Aram => GameModeKind.Aram,
                _ when IsAram(queueId, gameMode, mapId) => GameModeKind.Aram,
                _ when queueId > 0 => GameModeKind.Matchmade,
                _ => GameModeKind.Unknown
            };
        }

        public static GameModeKind Classify(LiveMatchSnapshot snapshot)
        {
            if (snapshot is null)
            {
                return GameModeKind.Unknown;
            }

            var gameData = snapshot.GameflowSession?.GameData;
            var lobby = snapshot.Lobby?.GameConfig;
            var matchmakingQueueId = snapshot.Matchmaking?.Queue?.Id ?? 0;

            // A concrete Hextech ARAM signal from any live endpoint wins over
            // transitional or stale ordinary ARAM data from another endpoint.
            if (IsHextechAram(gameData?.QueueId ?? 0, gameData?.GameMode) ||
                IsHextechAram(lobby?.QueueId ?? 0, lobby?.GameMode) ||
                IsHextechAramQueue(matchmakingQueueId))
            {
                return GameModeKind.HextechAram;
            }

            if ((gameData?.QueueId ?? 0) > 0)
            {
                return Classify(
                    gameData.QueueId,
                    gameData.GameMode,
                    gameData.MapId);
            }

            if ((lobby?.QueueId ?? 0) > 0)
            {
                return Classify(
                    lobby.QueueId,
                    lobby.GameMode,
                    lobby.MapId);
            }

            if (matchmakingQueueId > 0)
            {
                return Classify(matchmakingQueueId);
            }

            // Only consult source metadata without a queue ID after all later
            // sources have also failed to provide a concrete queue.
            var gameflowKind = Classify(
                0,
                gameData?.GameMode,
                gameData?.MapId ?? 0);
            return gameflowKind != GameModeKind.Unknown
                ? gameflowKind
                : Classify(0, lobby?.GameMode, lobby?.MapId ?? 0);
        }

        public static int ResolveQueueId(LiveMatchSnapshot snapshot)
        {
            var gameflowQueueId = snapshot?.GameflowSession?.GameData?.QueueId ?? 0;
            var lobbyQueueId = snapshot?.Lobby?.GameConfig?.QueueId ?? 0;
            var matchmakingQueueId = snapshot?.Matchmaking?.Queue?.Id ?? 0;

            if (IsHextechAramQueue(gameflowQueueId))
            {
                return gameflowQueueId;
            }

            if (IsHextechAramQueue(lobbyQueueId))
            {
                return lobbyQueueId;
            }

            if (IsHextechAramQueue(matchmakingQueueId))
            {
                return matchmakingQueueId;
            }

            if (gameflowQueueId > 0)
            {
                return gameflowQueueId;
            }

            return lobbyQueueId > 0 ? lobbyQueueId : matchmakingQueueId;
        }

        public static bool IsHextechAram(
            int queueId,
            string gameMode = null)
        {
            return IsHextechAramQueue(queueId) ||
                string.Equals(gameMode, "KIWI", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsHextechAramQueue(int queueId)
        {
            return queueId is GameQueueIds.HextechAram or
                GameQueueIds.HextechAramGameflow;
        }

        public static bool IsAram(
            int queueId,
            string gameMode = null,
            int mapId = 0)
        {
            return IsHextechAram(queueId, gameMode) ||
                queueId == GameQueueIds.Aram ||
                mapId == 12 ||
                string.Equals(gameMode, "ARAM", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAram(LiveMatchSnapshot snapshot)
        {
            return Classify(snapshot) is GameModeKind.Aram or
                GameModeKind.HextechAram;
        }

        public static bool IsHextechAram(LiveMatchSnapshot snapshot)
        {
            return Classify(snapshot) == GameModeKind.HextechAram;
        }

        public static bool IsQuickMatchQueue(int queueId)
        {
            return queueId is GameQueueIds.RankedSoloDuo or
                GameQueueIds.RankedFlex or
                GameQueueIds.Aram or
                GameQueueIds.HextechAram;
        }
    }
}
