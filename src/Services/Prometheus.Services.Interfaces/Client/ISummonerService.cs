using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface ISummonerService
    {
        Task<string> GetCurrentSummoner();

        Task<string> SearchSummonerByName(string nickname);

        Task<string> SearchSummonerByPuuid(string id);
    }
}
