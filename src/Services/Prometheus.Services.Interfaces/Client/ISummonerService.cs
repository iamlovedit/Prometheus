using Prometheus.Core.Models;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface ISummonerService
    {
        Task<SummonerAccount> GetCurrentSummoner();

        Task<SummonerAccount> SearchSummonerByName(string nickname);

        Task<SummonerAccount> SearchSummonerByPuuid(string id);

        Task<string> GetRankStatsByPuuid(string puuid);
    }
}
