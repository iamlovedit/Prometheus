using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Generic;
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

        public async Task<List<ChampionMastery>> GetChampionMasteriesAsync(string puuid, int count)
        {
            var json = await _httpService.GetAsync($"lol-collections/v1/inventories/{puuid}/champion-mastery/top?limit={count}");
            if (!string.IsNullOrEmpty(json))
            {
                return JObject.Parse(json)["masteries"].ToObject<List<ChampionMastery>>();
            }
            return null;
        }

        public async Task<string> GetBackdorpByIdAsync(long summonerId)
        {
            return await _httpService.GetAsync($"lol-collections/v1/inventories/{summonerId}/backdrop");
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

        public async Task<string> GetMatchsPageAsync(string puuid, int start, int end)
        {
            return await _httpService.GetAsync(string.Format($"lol-match-history/v1/products/lol/{puuid}/matches", puuid),
            [
               $"begIndex={start}",
               $"endIndex={end}",
            ]);
        }

        public async Task<List<Match>> GetMatchsAsync(string puuid, int start, int end)
        {
            var mathchesJosn = await _httpService.GetAsync(string.Format($"lol-match-history/v1/products/lol/{puuid}/matches", puuid),
            [
               $"begIndex={start}",
               $"endIndex={end}",
            ]);

            if (!string.IsNullOrEmpty(mathchesJosn))
            {
                var jObject = JObject.Parse(mathchesJosn);
                return jObject["games"]["games"].ToObject<List<Match>>();
            }
            return default;
        }
    }
}
