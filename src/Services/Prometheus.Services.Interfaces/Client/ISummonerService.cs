using Prometheus.Core.Models;
using System.Collections.Generic;
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

        Task<List<ChampionMastery>> GetChampionMasteriesAsync(string puuid,int count);

        Task<string> GetMatchsPageAsync(string puuid, int start, int end);
    }
}
