using Prometheus.Core.Models;

namespace Prometheus.Services.Interfaces.Client
{
    public interface ISummonerService
    {
        /// <summary>
        /// Gets the signed-in account from <c>lol-summoner/v1/current-summoner</c>.
        /// Returns <see langword="null"/> when LCU is unavailable and supports cancellation.
        /// </summary>
        Task<SummonerAccount> GetCurrentSummoner(CancellationToken cancellationToken = default);

        Task<SummonerAccount> SearchSummonerByName(string nickname);

        /// <summary>
        /// Gets an account from <c>lol-summoner/v2/summoners/puuid/{puuid}</c>.
        /// Returns <see langword="null"/> when LCU is unavailable and supports cancellation.
        /// </summary>
        Task<SummonerAccount> SearchSummonerByPuuid(string id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets ranked stats from <c>lol-ranked/v1/ranked-stats/{puuid}</c>.
        /// Returns <see langword="null"/> when LCU is unavailable and supports cancellation.
        /// </summary>
        Task<string> GetRankStatsByPuuid(string puuid,
            CancellationToken cancellationToken = default);

        Task<string> GetBackdorpByIdAsync(long summonerId);

        /// <summary>
        /// Gets the 20 most recent matches from
        /// <c>lol-match-history/v1/products/lol/{puuid}/matches</c>.
        /// All consumers use the same 0-19 window because the LCU caches the first response
        /// for this path without considering later pagination parameters. The Home dashboard
        /// displays only the first five matches from this result. Preserves failures separately
        /// from successful empty responses and supports cancellation.
        /// </summary>
        Task<MatchHistoryQueryResult> GetMatchHistoryAsync(string puuid,
            CancellationToken cancellationToken = default);
    }
}
