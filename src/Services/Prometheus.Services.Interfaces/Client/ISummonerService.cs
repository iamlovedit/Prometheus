using Prometheus.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface ISummonerService
    {
        Task<SummonerAccount> GetCurrentSummoner();

        Task<SummonerAccount> SearchSummonerByName(string nickname);

        Task<SummonerAccount> SearchSummonerByPuuid(string id);

        Task<string> GetRankStatsByPuuid(string puuid);

        Task<string> GetRecentMatchesByPuuid(string puuid);

        Task<string> GetBackdorpByIdAsync(long summonerId);

        /// <summary>
        /// Gets a page of matches from
        /// <c>lol-match-history/v1/products/lol/{puuid}/matches</c>.
        /// Returns an empty list when LCU is unavailable or the response contains no games.
        /// </summary>
        Task<List<Match>> GetMatchesAsync(string puuid, int start, int end,
            CancellationToken cancellationToken = default);
    }
}
