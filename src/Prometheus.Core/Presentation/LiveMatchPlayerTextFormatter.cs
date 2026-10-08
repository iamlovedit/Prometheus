using Prometheus.Core.Models;

namespace Prometheus.Core.Presentation
{
    public static class LiveMatchPlayerTextFormatter
    {
        public static string FormatDisplayName(
            LiveMatchPlayerSnapshot player,
            string localFallback,
            string unknownFallback)
        {
            return FormatDisplayName(
                player,
                () => localFallback,
                () => unknownFallback);
        }

        public static string FormatDisplayName(
            LiveMatchPlayerSnapshot player,
            Func<string> localFallback,
            Func<string> unknownFallback)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(localFallback);
            ArgumentNullException.ThrowIfNull(unknownFallback);

            var summoner = player.Summoner;
            if (summoner is not null)
            {
                var gameName = FirstNotEmpty(
                    summoner.GameName,
                    summoner.DisplayName,
                    summoner.SummonerName);
                if (!string.IsNullOrWhiteSpace(gameName))
                {
                    return string.IsNullOrWhiteSpace(summoner.TagLine)
                        ? gameName
                        : $"{gameName}#{summoner.TagLine}";
                }
            }

            if (!string.IsNullOrWhiteSpace(player.DisplayName))
            {
                return player.DisplayName;
            }

            return player.IsLocalPlayer ? localFallback() : unknownFallback();
        }

        public static string FormatRank(
            Rank rank,
            Func<string, string, string> text)
        {
            ArgumentNullException.ThrowIfNull(text);

            if (rank is null || rank.Tier == Tier.UNRANKED)
            {
                return text("Match.Live.Rank.Unranked", "Unranked");
            }

            var tierKey = rank.Tier switch
            {
                Tier.IRON => "Career.Rank.Tier.Iron",
                Tier.BRONZE => "Career.Rank.Tier.Bronze",
                Tier.SILVER => "Career.Rank.Tier.Silver",
                Tier.GOLD => "Career.Rank.Tier.Gold",
                Tier.PLATINUM => "Career.Rank.Tier.Platinum",
                Tier.EMERALD => "Career.Rank.Tier.Emerald",
                Tier.DIAMOND => "Career.Rank.Tier.Diamond",
                Tier.MASTER => "Career.Rank.Tier.Master",
                Tier.GRANDMASTER => "Career.Rank.Tier.Grandmaster",
                Tier.CHALLENGER => "Career.Rank.Tier.Challenger",
                _ => "Career.Rank.Tier.Unranked"
            };
            var tier = text(tierKey, rank.Tier.ToString());
            var division = string.IsNullOrWhiteSpace(rank.Division) ||
                string.Equals(rank.Division, nameof(Division.NA),
                    StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" {rank.Division}";
            return $"{tier}{division} · {rank.LeaguePoints} LP";
        }

        private static string FirstNotEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
                string.Empty;
        }
    }
}
