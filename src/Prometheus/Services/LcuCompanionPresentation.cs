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
            return snapshot?.GameflowSession?.GameData?.QueueId ??
                   snapshot?.Lobby?.GameConfig?.QueueId ??
                   snapshot?.Matchmaking?.Queue?.Id ??
                   0;
        }

        public static LcuCompanionMode GetMode(LiveMatchSnapshot snapshot)
        {
            var queueId = GetQueueId(snapshot);
            return queueId switch
            {
                GameQueueIds.RankedSoloDuo => LcuCompanionMode.RankedSoloDuo,
                GameQueueIds.RankedFlex => LcuCompanionMode.RankedFlex,
                GameQueueIds.Aram => LcuCompanionMode.Aram,
                GameQueueIds.HextechAram => LcuCompanionMode.HextechAram,
                _ when IsAram(snapshot) => LcuCompanionMode.Aram,
                _ => LcuCompanionMode.Matchmade
            };
        }

        public static bool IsAram(LiveMatchSnapshot snapshot)
        {
            var gameData = snapshot?.GameflowSession?.GameData;
            if (gameData is not null &&
                (gameData.QueueId is GameQueueIds.Aram or GameQueueIds.HextechAram ||
                 gameData.MapId == 12 ||
                 string.Equals(gameData.GameMode, "ARAM",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var lobby = snapshot?.Lobby?.GameConfig;
            return lobby is not null &&
                   (lobby.QueueId is GameQueueIds.Aram or GameQueueIds.HextechAram ||
                    lobby.MapId == 12 ||
                    string.Equals(lobby.GameMode, "ARAM",
                        StringComparison.OrdinalIgnoreCase));
        }

        public static int GetLocalChampionId(LiveMatchSnapshot snapshot)
        {
            var championSelect = snapshot?.ChampionSelect;
            return championSelect?.MyTeam?.FirstOrDefault(member =>
                    member?.CellId == championSelect.LocalPlayerCellId)?.ChampionId ?? 0;
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
    }
}
