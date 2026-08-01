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
        /// The transport layer reads a stable 200-match LCU window and returns the first five
        /// because some LCU builds cache this endpoint without considering its query string.
        /// Returns an empty list when LCU is unavailable or the response contains no games.
        /// Supports cancellation.
        /// </summary>
        Task<List<Match>> GetHomeRecentMatchesAsync(string puuid,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the 20 recent matches displayed on a summoner's Career page from
        /// <c>lol-match-history/v1/products/lol/{puuid}/matches</c>.
        /// The transport layer reads a stable 200-match LCU window and returns the first 20
        /// to avoid path-only LCU cache pollution from the Home request.
        /// Preserves the distinction between a successful empty response and an
        /// unavailable/malformed LCU response. Supports cancellation.
        /// </summary>
        Task<MatchHistoryQueryResult> GetSummonerRecentMatchesAsync(string puuid,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the 200 matches loaded when entering the Match History page from
        /// <c>lol-match-history/v1/products/lol/{puuid}/matches</c>.
        /// The page displays this result in local 20-match pages. Preserves failures
        /// separately from successful empty responses and supports cancellation.
        /// </summary>
        Task<MatchHistoryQueryResult> GetMatchHistoryAsync(string puuid,
            CancellationToken cancellationToken = default);
    }
}
