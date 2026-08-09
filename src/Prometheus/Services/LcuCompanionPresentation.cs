using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Desktop.Services
{
    public enum LcuCompanionMode
    {
        Matchmade,
        RankedSoloDuo,
        RankedFlex,
        Aram,
        HextechAram
    }

    public static class LcuCompanionPresentation
    {
        public static int GetQueueId(LiveMatchSnapshot snapshot)
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

            if (lobbyQueueId > 0)
            {
                return lobbyQueueId;
            }

            return matchmakingQueueId > 0 ? matchmakingQueueId : 0;
        }

        public static LcuCompanionMode GetMode(LiveMatchSnapshot snapshot)
        {
            var queueId = GetQueueId(snapshot);
            return queueId switch
            {
                GameQueueIds.RankedSoloDuo => LcuCompanionMode.RankedSoloDuo,
                GameQueueIds.RankedFlex => LcuCompanionMode.RankedFlex,
                GameQueueIds.Aram => LcuCompanionMode.Aram,
                _ when IsHextechAramQueue(queueId) => LcuCompanionMode.HextechAram,
                _ when IsHextechAram(snapshot) => LcuCompanionMode.HextechAram,
                _ when IsAram(snapshot) => LcuCompanionMode.Aram,
                _ => LcuCompanionMode.Matchmade
            };
        }

        public static bool IsAram(LiveMatchSnapshot snapshot)
        {
            var gameData = snapshot?.GameflowSession?.GameData;
            if (gameData is not null &&
                (gameData.QueueId == GameQueueIds.Aram ||
                 IsHextechAramQueue(gameData.QueueId) ||
                 gameData.MapId == 12 ||
                 string.Equals(gameData.GameMode, "ARAM",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var lobby = snapshot?.Lobby?.GameConfig;
            return lobby is not null &&
                   (lobby.QueueId == GameQueueIds.Aram ||
                    IsHextechAramQueue(lobby.QueueId) ||
                    lobby.MapId == 12 ||
                    string.Equals(lobby.GameMode, "ARAM",
                        StringComparison.OrdinalIgnoreCase));
        }

        public static int GetLocalChampionId(LiveMatchSnapshot snapshot)
        {
            var championSelect = snapshot?.ChampionSelect;
            var member = championSelect?.MyTeam?.FirstOrDefault(item =>
                item?.CellId == championSelect.LocalPlayerCellId);
            return member?.ChampionId > 0
                ? member.ChampionId
                : member?.ChampionPickIntent ?? 0;
        }

        public static string GetLocalAssignedPosition(LiveMatchSnapshot snapshot)
        {
            var championSelect = snapshot?.ChampionSelect;
            var position = championSelect?.MyTeam?.FirstOrDefault(member =>
                    member?.CellId == championSelect.LocalPlayerCellId)
                ?.AssignedPosition;
            return position?.Trim().ToLowerInvariant() switch
            {
                "middle" => "mid",
                "utility" => "support",
                "bot" or "adc" => "bottom",
                "top" => "top",
                "jungle" => "jungle",
                "mid" => "mid",
                "bottom" => "bottom",
                "support" => "support",
                _ => string.Empty
            };
        }

        public static int GetAramAutomationTarget(
            LiveMatchSnapshot snapshot,
            IReadOnlyList<int> preferredChampionIds)
        {
            var championSelect = snapshot?.ChampionSelect;
            var preferred = Normalize(preferredChampionIds);
            if (championSelect is null || preferred.Length == 0 || !IsAram(snapshot))
            {
                return 0;
            }

            var currentChampionId = GetLocalChampionId(snapshot);
            if (currentChampionId > 0 && preferred.Contains(currentChampionId))
            {
                return currentChampionId;
            }

            if (!championSelect.BenchEnabled)
            {
                return 0;
            }

            var bench = (championSelect.BenchChampions ?? [])
                .Where(champion => champion is not null && champion.ChampionId > 0)
                .Select(champion => champion.ChampionId)
                .ToHashSet();
            return preferred.FirstOrDefault(bench.Contains);
        }

        public static ChampionSelectActionSnapshot FindLocalAction(
            LiveMatchSnapshot snapshot,
            string actionType)
        {
            var championSelect = snapshot?.ChampionSelect;
            if (championSelect is null || string.IsNullOrWhiteSpace(actionType))
            {
                return null;
            }

            return (championSelect.Actions ?? [])
                .Where(round => round is not null)
                .SelectMany(round => round)
                .Where(action => action is not null &&
                    action.ActorCellId == championSelect.LocalPlayerCellId &&
                    string.Equals(action.Type, actionType,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(action => action.IsInProgress)
                .ThenByDescending(action => action.Completed)
                .ThenByDescending(action => action.Id)
                .FirstOrDefault();
        }

        public static int GetChampionSelectAutomationTarget(
            LiveMatchSnapshot snapshot,
            string actionType,
            IReadOnlyList<int> preferredChampionIds)
        {
            var action = FindLocalAction(snapshot, actionType);
            if (action?.ChampionId > 0)
            {
                return action.ChampionId;
            }

            return Normalize(preferredChampionIds).FirstOrDefault();
        }

        private static int[] Normalize(IEnumerable<int> championIds)
        {
            return championIds?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
        }

        private static bool IsHextechAramQueue(int queueId)
        {
            return queueId is GameQueueIds.HextechAram or
                GameQueueIds.HextechAramGameflow;
        }

        private static bool IsHextechAram(LiveMatchSnapshot snapshot)
        {
            var gameData = snapshot?.GameflowSession?.GameData;
            if (gameData is not null &&
                (IsHextechAramQueue(gameData.QueueId) ||
                 string.Equals(gameData.GameMode, "KIWI",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var lobby = snapshot?.Lobby?.GameConfig;
            return lobby is not null &&
                   (IsHextechAramQueue(lobby.QueueId) ||
                    string.Equals(lobby.GameMode, "KIWI",
                        StringComparison.OrdinalIgnoreCase));
        }
    }
}
