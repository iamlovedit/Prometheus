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

        Task<List<ProfileIcon>> GetProfileIconsAsync();

        Task<string> GetProfileIconByIdAsync(int id);

        Task<string> GetChampoinIconByIdAsync(int championId);

        Task<string> GetEquipmentIconByIdAsync(int equipmentId);

        Task<string> GetSpellIconByIdAsync(int spellId);

        Task<string> GetBackgroundSkinByIdAsync(int skinId);

        Task<string> GetPerkIconByIdAsync(int perkId);

        Task<string> GetBackgroundSkinId();

        Task SetBackgroundSkinId(int id);

    }
}
