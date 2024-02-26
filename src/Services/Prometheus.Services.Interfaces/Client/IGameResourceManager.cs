using Prometheus.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IGameResourceManager
    {
        Task<List<Equipment>> GetEquipmentsAsync();

        Task<List<Perk>> GetPerksAsync();

        Task<List<ChampionSummary>> GetChampionSummarysAsync();

        Task<Dictionary<string, Skin>> GetSkinsAsync();

        Task<List<Spell>> GetSpellsAsync();

        Task<string> GetProfileIconById(int id);

        Task<string> GetBackgroundSkinId();

        Task SetBackgroundSkinId(int id);

    }
}
