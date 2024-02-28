using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Threading.Tasks;
using System.Web;

namespace Prometheus.Services.Client
{
    public class SummonerService : ISummonerService
    {
        private readonly IHttpService _httpService;
        public SummonerService(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task<SummonerAccount> GetCurrentSummoner()
        {
            return await _httpService.GetAsync<SummonerAccount>("lol-summoner/v1/current-summoner");
        }

        public async Task<string> GetRankStatsByPuuid(string puuid)
        {
            return await _httpService.GetAsync($"lol-ranked/v1/ranked-stats/{puuid}");
        }

        public async Task<string> GetRecentMatchesByPuuid(string puuid)
        {
            return await _httpService.GetAsync($"lol-match-history/v1/products/lol/{puuid}/matches");
        }

        public async Task<SummonerAccount> SearchSummonerByName(string nickname)
        {
            return await _httpService.GetAsync<SummonerAccount>("lol-summoner/v1/summoners",
            [
               $"name={HttpUtility.UrlEncode(nickname)}"
            ]);
        }

        public async Task<SummonerAccount> SearchSummonerByPuuid(string puuid)
        {
            return await _httpService.GetAsync<SummonerAccount>($"lol-summoner/v2/summoners/puuid/{puuid}");
        }
    }
}
