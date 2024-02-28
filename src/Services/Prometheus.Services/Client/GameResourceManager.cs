using Prism.Ioc;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Prometheus.Services.Client
{
    public class GameResourceManager : IGameResourceManager
    {
        private readonly IHttpService _httpService;
        private readonly IContainerExtension _containerExtension;
        private List<Equipment> _equipments;
        private List<Spell> _spells;
        private Dictionary<string, Skin> _skinMap;
        private List<Perk> _perks;

        public GameResourceManager(IHttpService httpService, IContainerExtension containerExtension)
        {
            _httpService = httpService;
            _containerExtension = containerExtension;
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

        public async Task<string> GetProfileIconByIdAsync(int id)
        {
            var directory = GetDirectory(ParameterNames.ProfileIcon);
            var iconPath = Path.Combine(directory, $"{id}.jpg");
            if (!File.Exists(iconPath))
            {
                var buffer = await _httpService.GetByteArrayResponseAsync(HttpMethod.Get, $"lol-game-data/assets/v1/profile-icons/{id}.jpg");
                await File.WriteAllBytesAsync(iconPath, buffer);
            }
            return iconPath;
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

        public async Task<List<ProfileIcon>> GetProfileIconsAsync()
        {
            return await _httpService.GetAsync<List<ProfileIcon>>("lol-game-data/assets/v1/profile-icons.json");
        }

        public async Task<string> GetChampoinIconByIdAsync(int championId)
        {
            var directory = GetDirectory(ParameterNames.ChampoinIcon);
            var iconPath = Path.Combine(directory, $"{championId}.png");
            if (!File.Exists(iconPath))
            {
                await DownloadAsync($"lol-game-data/assets/v1/champion-icons/{championId}.png", iconPath);
            }
            return iconPath;
        }

        public async Task<string> GetEquipmentIconByIdAsync(int equipmentId)
        {
            var directory = GetDirectory(ParameterNames.Equipments);
            var iconPath = Path.Combine(directory, $"{equipmentId}.png");
            if (!File.Exists(iconPath))
            {
                if (_equipments is null)
                {
                    _equipments = await GetEquipmentsAsync();
                }
                var equipment = _equipments.FirstOrDefault(e => e.Id == equipmentId);

                if (equipment is null)
                {
                    iconPath = Path.Combine(directory, "gp_ui_placeholder.png");
                    if (!File.Exists(iconPath))
                    {
                        await DownloadAsync("lol-game-data/assets/ASSETS/Items/Icons2D/gp_ui_placeholder.png", iconPath);
                        return iconPath;
                    }
                    return iconPath;
                }
                else
                {
                    await DownloadAsync(equipment.IconPath, iconPath);
                    return iconPath;
                }
            }
            return iconPath;
        }

        public async Task<string> GetSpellIconByIdAsync(int spellId)
        {
            var directory = GetDirectory(ParameterNames.Spells);
            var iconPath = Path.Combine(directory, $"{spellId}.png");
            if (!File.Exists(iconPath))
            {
                if (_spells is null)
                {
                    _spells = await GetSpellsAsync();
                }
                var spell = _spells.FirstOrDefault(s => s.Id == spellId);
                if (spell is null)
                {
                    iconPath = Path.Combine(directory, "summoner_empty.png");
                    if (!File.Exists(iconPath))
                    {
                        await DownloadAsync("lol-game-data/assets/data/spells/icons2d/summoner_empty.png", iconPath);
                        return iconPath;
                    }
                    return iconPath;
                }
                else
                {
                    await DownloadAsync(spell.IconPath, iconPath);
                    return iconPath;
                }
            }
            return iconPath;
        }

        public async Task<string> GetBackgroundSkinByIdAsync(int skinId)
        {
            var directory = GetDirectory(ParameterNames.Skins);
            var skinPath = Path.Combine(directory, $"{skinId}.jpg");
            if (!File.Exists(skinPath))
            {
                if (_skinMap is null)
                {
                    _skinMap = await GetSkinsAsync();
                }
                if (_skinMap.TryGetValue(skinId.ToString(), out var skin))
                {
                    await DownloadAsync(skin.SplashPath, skinPath);
                    return skinPath;
                }
                else
                {
                    skinId = 157000; //default Yasuo  hasaki!
                    return await GetBackgroundSkinByIdAsync(skinId);
                }
            }
            return skinPath;
        }

        public async Task<string> GetPerkIconByIdAsync(int perkId)
        {
            var directory = GetDirectory(ParameterNames.Perks);
            var iconPath = Path.Combine(directory, $"{perkId}.png");
            if (!File.Exists(iconPath))
            {
                if (_perks is null)
                {
                    _perks = await GetPerksAsync();
                }
                var perk = _perks.FirstOrDefault(p => p.Id == perkId);
                //TODO:default icon
                await DownloadAsync(perk.IconPath, iconPath);
            }
            return iconPath;
        }

        private string GetDirectory(string directoryName)
        {
            var directory = _containerExtension.Resolve<string>(directoryName);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return directory;
        }

        private async Task DownloadAsync(string url, string filePath)
        {
            var buffer = await _httpService.GetByteArrayResponseAsync(HttpMethod.Get, url);
            await File.WriteAllBytesAsync(filePath, buffer);
        }
    }
}
