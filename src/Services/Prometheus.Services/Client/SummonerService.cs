using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Services.Client
{
    public class SummonerService : ISummonerService
    {
        private readonly IHttpService _httpService;
        public SummonerService(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task<string> GetCurrentSummoner()
        {
            return await _httpService.GetAsync("lol-summoner/v1/current-summoner");
        }

        public async Task<string> SearchSummonerByName(string nickname)
        {
            return await _httpService.GetAsync("lol-summoner/v1/summoners",
            [
               $"name={nickname}"
            ]);
        }

        public async Task<string> SearchSummonerByPuuid(string puuid)
        {
            return await _httpService.GetAsync($"lol-summoner/v2/summoners/puuid/{puuid}");
        }
    }
}
