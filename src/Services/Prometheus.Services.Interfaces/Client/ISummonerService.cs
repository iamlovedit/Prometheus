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

        Task<string> GetRecentMatchesByPuuid(string puuid);

        Task<string> GetBackdorpByIdAsync(long summonerId);

        /// <summary>
        /// Gets a page of matches from
        /// <c>lol-match-history/v1/products/lol/{puuid}/matches</c>.
        /// The implementation retrieves a stable 200-match LCU window and slices it locally because
        /// some clients cache this endpoint without including its query string in the cache key.
        /// Returns an empty list when LCU is unavailable or the response contains no games.
        /// </summary>
        Task<List<Match>> GetMatchesAsync(string puuid, int start, int end,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a match-history page while preserving the distinction between
        /// a successful empty page and an unavailable/malformed LCU response.
        /// Supports cancellation.
        /// </summary>
        Task<MatchHistoryQueryResult> GetMatchesResultAsync(string puuid,
            int start, int end, CancellationToken cancellationToken = default);
    }
}
