using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
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
        public async Task<List<Equipment>> GetEquipmentsAsync()
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

        public async Task<string> GetProfileIconById(int id)
        {
            return await _httpService.GetAsync($"lol-game-data/assets/v1/profile-icons/{id}.jpg");
        }

        public async Task<string> GetBackgroundSkinId()
        {
            return await _httpService.GetAsync("lol-summoner/v1/current-summoner/summoner-profile");
        }

        public async Task SetBackgroundSkinId(int id)
        {
            var body = new
            {
                key = "backgroundSkinId",
                value = id
            };
            await _httpService.PostAsync("lol-summoner/v1/current-summoner/summoner-profile", body);
        }

        public async Task<List<Spell>> GetSpellsAsync()
        {
            return await _httpService.GetAsync<List<Spell>>("lol-game-data/assets/v1/summoner-spells.json");
        }
    }
}
