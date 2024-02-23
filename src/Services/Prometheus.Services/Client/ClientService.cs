using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Models;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Prometheus.Services.Client
{
    public class ClientService : IClientService
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool PathCanonicalize(StringBuilder dst, string src);

        private readonly string _processUrl = "process-control/v1/process/";
        private readonly IHttpService _httpService;
        public ClientService(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task<string> GetInstallLocation()
        {
            var path = await _httpService.GetAsync("data-store/v1/install-dir");
            return ConvertPath(path);
        }

        public async Task QuitClientAsync()
        {
            await _httpService.PostAsync($"{_processUrl}quit", null);
        }

        private static string ConvertPath(string path)
        {
            var pathParts = path.Split(':');
            return path.Replace(pathParts[0], pathParts[0].ToUpper()).Replace(@"\\", @"\");
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

        public async Task<string> GetQueuesAsync()
        {
            return await _httpService.GetAsync("lol-game-queues/v1/queues");
        }

        public async Task<Dictionary<string,Skin>> GetSkinsAsync()
        {
            return await _httpService.GetAsync<Dictionary<string, Skin>>("lol-game-data/assets/v1/skins.json");
        }
    }
}
