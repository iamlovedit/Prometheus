using Prometheus.Services.Interfaces.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IGameResourceManager
    {
        Task<List<Equipment>> GetItemsAsync();

        Task<List<Perk>> GetPerksAsync();

        Task<List<ChampionSummary>> GetChampionSummarysAsync();

        Task<Dictionary<string, Skin>> GetSkinsAsync();
    }
}
