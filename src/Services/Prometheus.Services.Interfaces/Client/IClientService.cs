using Prometheus.Services.Interfaces.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IClientService
    {
        Task<string> GetInstallLocation();

        Task QuitClientAsync();

        Task<List<Equipment>> GetItemsAsync();

        Task<List<Perk>> GetPerksAsync();

        Task<List<ChampionSummary>> GetChampionSummarysAsync();

        Task<string> GetQueuesAsync();

        Task<Dictionary<string, Skin>> GetSkinsAsync();

    }
}
