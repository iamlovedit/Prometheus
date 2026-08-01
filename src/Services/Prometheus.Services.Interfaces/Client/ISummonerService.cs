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
        /// Gets the five recent matches used by the Home dashboard from
        /// <c>lol-match-history/v1/products/lol/{puuid}/matches</c>.
        /// Returns an empty list when LCU is unavailable or the response contains no games.
        /// Supports cancellation.
        /// </summary>
        Task<List<Match>> GetHomeRecentMatchesAsync(string puuid,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a 20-match page from
        /// <c>lol-match-history/v1/products/lol/{puuid}/matches</c> while preserving
        /// the distinction between a successful empty page and an unavailable/malformed
        /// LCU response. Page indexes are one-based and cancellation is supported.
        /// </summary>
        Task<MatchHistoryQueryResult> GetMatchHistoryPageAsync(string puuid,
            int pageIndex, CancellationToken cancellationToken = default);
    }
}
