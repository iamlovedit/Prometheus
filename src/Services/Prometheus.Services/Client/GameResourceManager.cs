using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Services.Client
{
    public class GameResourceManager : IGameResourceManager
    {
        private readonly IHttpService _httpService;

        public GameResourceManager(IHttpService httpService)
        {
            _httpService = httpService;
        }
        public async Task<List<Equipment>> GetItemsAsync()
        {
            return await _httpService.GetAsync<List<Equipment>>("lol-game-data/assets/v1/items.json");
        }

        public async Task<List<Perk>> GetPerksAsync()
        {
            return await _httpService.GetAsync<List<Perk>>("lol-game-data/assets/v1/perks.json");
        }

        public async Task<List<ChampionSummary>> GetChampionSummarysAsync()
        {
            return await _httpService.GetAsync<List<ChampionSummary>>("lol-game-data/assets/v1/champion-summary.json");
        }

        public async Task<Dictionary<string, Skin>> GetSkinsAsync()
        {
            return await _httpService.GetAsync<Dictionary<string, Skin>>("lol-game-data/assets/v1/skins.json");
        }
    }
}
