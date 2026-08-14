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
            return GameModeResolver.ResolveQueueId(snapshot);
        }

        public static LcuCompanionMode GetMode(LiveMatchSnapshot snapshot)
        {
            return GameModeResolver.Classify(snapshot) switch
            {
                GameModeKind.RankedSoloDuo => LcuCompanionMode.RankedSoloDuo,
                GameModeKind.RankedFlex => LcuCompanionMode.RankedFlex,
                GameModeKind.Aram => LcuCompanionMode.Aram,
                GameModeKind.HextechAram => LcuCompanionMode.HextechAram,
                _ => LcuCompanionMode.Matchmade
            };
        }

        public static bool IsAram(LiveMatchSnapshot snapshot)
        {
            return GameModeResolver.IsAram(snapshot);
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

    }
}
