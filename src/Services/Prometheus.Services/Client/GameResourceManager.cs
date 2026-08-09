using Prism.Ioc;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using Serilog.Core;
using System.Collections.Concurrent;

namespace Prometheus.Services.Client
{
    public class GameResourceManager : IGameResourceManager
    {
        private const int DefaultBackgroundSkinId = 157000;
        private const int MaximumConcurrentSkinDownloads = 4;

        private readonly IHttpService _httpService;
        private readonly IContainerExtension _containerExtension;
        private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _metadataLoads =
            new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _fileLoads =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, Lazy<Task<List<Skin>>>>
            _skinLoadsByChampion = new();

        public GameResourceManager(IHttpService httpService, IContainerExtension containerExtension)
        {
            _httpService = httpService;
            _containerExtension = containerExtension;
        }
        public Task<List<Equipment>> GetEquipmentsAsync()
        {
            return GetMetadataAsync<List<Equipment>>(
                "lol-game-data/assets/v1/items.json");
        }

        public Task<List<Perk>> GetPerksAsync()
        {
            return GetMetadataAsync<List<Perk>>(
                "lol-game-data/assets/v1/perks.json");
        }

        public async Task<List<ChampionSummary>> GetChampionSummarysAsync()
        {
            var champions = await GetMetadataAsync<List<ChampionSummary>>(
                    "lol-game-data/assets/v1/champion-summary.json")
                .ConfigureAwait(false);
            return champions?.Select(CloneChampionSummary).ToList();
        }

        public async Task<string> GetProfileIconByIdAsync(int id)
        {
            var directory = GetDirectory(ParameterNames.ProfileIcon);
            var iconPath = Path.Combine(directory, $"{id}.jpg");
            if (!File.Exists(iconPath))
            {
                try
                {
                    var result = await EnsureDownloadedAsync(
                        $"lol-game-data/assets/v1/profile-icons/{id}.jpg", iconPath);
                    if (string.IsNullOrWhiteSpace(result))
                    {
                        return default;
                    }
                }
                catch (Exception exception)
                {
                    Log.Warning(exception,
                        "Unable to load profile icon {ProfileIconId}", id);
                    return default;
                }

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

        public Task<List<Spell>> GetSpellsAsync()
        {
            return GetMetadataAsync<List<Spell>>(
                "lol-game-data/assets/v1/summoner-spells.json");
        }

        public async Task<List<ProfileIcon>> GetProfileIconsAsync()
        {
            var icons = await GetMetadataAsync<List<ProfileIcon>>(
                    "lol-game-data/assets/v1/profile-icons.json")
                .ConfigureAwait(false);
            return icons?.Select(icon => new ProfileIcon
            {
                Id = icon.Id,
                IconPath = icon.IconPath
            }).ToList();
        }

        public async Task<string> GetChampoinIconByIdAsync(int championId)
        {
            var directory = GetDirectory(ParameterNames.ChampoinIcon);
            var iconPath = Path.Combine(directory, $"{championId}.png");
            if (File.Exists(iconPath))
            {
                return iconPath;
            }

            try
            {
                return await EnsureDownloadedAsync(
                        $"lol-game-data/assets/v1/champion-icons/{championId}.png", iconPath)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to load champion icon {ChampionId}", championId);
                return default;
            }
        }

        public async Task<string> GetEquipmentIconByIdAsync(int equipmentId)
        {
            var directory = GetDirectory(ParameterNames.Equipments);
            var iconPath = Path.Combine(directory, $"{equipmentId}.png");
            if (!File.Exists(iconPath))
            {
                var equipments = await GetEquipmentsAsync();
                var equipment = equipments?.FirstOrDefault(e => e.Id == equipmentId);

                if (equipment is null)
                {
                    iconPath = Path.Combine(directory, "gp_ui_placeholder.png");
                    if (!File.Exists(iconPath))
                    {
                        return await EnsureDownloadedAsync(
                            "lol-game-data/assets/ASSETS/Items/Icons2D/gp_ui_placeholder.png",
                            iconPath);
                    }
                    return iconPath;
                }
                else
                {
                    return await EnsureDownloadedAsync(equipment.IconPath, iconPath);
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
                var spells = await GetSpellsAsync();
                var spell = spells?.FirstOrDefault(s => s.Id == spellId);
                if (spell is null)
                {
                    iconPath = Path.Combine(directory, "summoner_empty.png");
                    if (!File.Exists(iconPath))
                    {
                        return await EnsureDownloadedAsync(
                            "lol-game-data/assets/data/spells/icons2d/summoner_empty.png",
                            iconPath);
                    }
                    return iconPath;
                }
                else
                {
                    return await EnsureDownloadedAsync(spell.IconPath, iconPath);
                }
            }
            return iconPath;
        }

        public async Task<string> GetBackgroundSkinByIdAsync(int skinId)
        {
            if (skinId <= 0)
            {
                skinId = DefaultBackgroundSkinId;
            }

            var directory = GetDirectory(ParameterNames.Skins);
            var skinPath = Path.Combine(directory, $"{skinId}.jpg");
            if (File.Exists(skinPath))
            {
                return skinPath;
            }

            try
            {
                var championId = skinId / 1000;
                var skins = await GetChampionSkinsAsync(championId);
                var skin = skins.FirstOrDefault(item => item.Id == skinId);
                if (skin is not null)
                {
                    return await EnsureDownloadedAsync(skin.SplashPath, skinPath);
                }

                if (skinId != DefaultBackgroundSkinId)
                {
                    return await GetBackgroundSkinByIdAsync(DefaultBackgroundSkinId);
                }
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load background skin {SkinId}", skinId);
                if (skinId != DefaultBackgroundSkinId)
                {
                    return await GetBackgroundSkinByIdAsync(DefaultBackgroundSkinId);
                }
            }

            return default;
        }

        public async Task<string> GetPerkIconByIdAsync(int perkId)
        {
            if (perkId <= 0)
            {
                return default;
            }

            var directory = GetDirectory(ParameterNames.Perks);
            var iconPath = Path.Combine(directory, $"{perkId}.png");
            if (File.Exists(iconPath))
            {
                return iconPath;
            }

            try
            {
                var perks = await GetPerksAsync().ConfigureAwait(false);
                if (perks is null || perks.Count == 0)
                {
                    Log.Warning("Unable to load perk metadata for perk {PerkId}", perkId);
                    return default;
                }

                var perk = perks.FirstOrDefault(p => p.Id == perkId);
                if (string.IsNullOrWhiteSpace(perk?.IconPath))
                {
                    Log.Warning("LCU perk metadata does not contain perk {PerkId}", perkId);
                    return default;
                }

                return await EnsureDownloadedAsync(perk.IconPath, iconPath)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load perk icon {PerkId}", perkId);
                return default;
            }
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

        private async Task<T> GetMetadataAsync<T>(string endpoint)
            where T : class, new()
        {
            var lazy = _metadataLoads.GetOrAdd(endpoint, key =>
                new Lazy<Task<object>>(
                    async () => await _httpService.GetAsync<T>(key).ConfigureAwait(false),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                var result = await lazy.Value.ConfigureAwait(false) as T;
                if (result is null)
                {
                    _metadataLoads.TryRemove(
                        new KeyValuePair<string, Lazy<Task<object>>>(endpoint, lazy));
                }

                return result;
            }
            catch
            {
                _metadataLoads.TryRemove(
                    new KeyValuePair<string, Lazy<Task<object>>>(endpoint, lazy));
                throw;
            }
        }

        private static ChampionSummary CloneChampionSummary(ChampionSummary source)
        {
            return new ChampionSummary
            {
                Id = source.Id,
                Name = source.Name,
                Alias = source.Alias,
                SquarePortraitPath = source.SquarePortraitPath,
                Roles = source.Roles?.ToList(),
                IconUri = source.IconUri
            };
        }

        private Task<string> EnsureDownloadedAsync(string url, string filePath)
        {
            if (File.Exists(filePath))
            {
                return Task.FromResult(filePath);
            }

            return AwaitDownloadAsync(url, filePath);
        }

        private async Task<string> AwaitDownloadAsync(string url, string filePath)
        {
            var lazy = _fileLoads.GetOrAdd(filePath, _ =>
                new Lazy<Task<string>>(
                    () => DownloadFileCoreAsync(url, filePath),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                return await lazy.Value.ConfigureAwait(false);
            }
            finally
            {
                _fileLoads.TryRemove(
                    new KeyValuePair<string, Lazy<Task<string>>>(filePath, lazy));
            }
        }

        private async Task<string> DownloadFileCoreAsync(string url, string filePath)
        {
            if (File.Exists(filePath))
            {
                return filePath;
            }

            var buffer = await _httpService.GetByteArrayResponseAsync(HttpMethod.Get, url)
                .ConfigureAwait(false);
            if (buffer is null || buffer.Length == 0)
            {
                return default;
            }

            await File.WriteAllBytesAsync(filePath, buffer).ConfigureAwait(false);
            return filePath;
        }

        public async Task<List<SkinBasic>> GetSkinsByChampionIdAsync(int championId)
        {
            if (championId <= 0)
            {
                return [];
            }

            try
            {
                var championSkins = await GetChampionSkinsAsync(championId);
                using var gate = new SemaphoreSlim(
                    MaximumConcurrentSkinDownloads,
                    MaximumConcurrentSkinDownloads);
                var skins = await Task.WhenAll(championSkins.Select(async skin =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var uri = await GetBackgroundSkinByIdAsync(skin.Id)
                            .ConfigureAwait(false);
                        return string.IsNullOrEmpty(uri)
                            ? null
                            : new SkinBasic
                            {
                                Id = skin.Id,
                                Name = skin.Name,
                                Uri = uri
                            };
                    }
                    finally
                    {
                        gate.Release();
                    }
                })).ConfigureAwait(false);

                return skins.Where(skin => skin is not null).ToList();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load skins for champion {ChampionId}", championId);
                return [];
            }
        }

        private async Task<List<Skin>> GetChampionSkinsAsync(int championId)
        {
            var lazy = _skinLoadsByChampion.GetOrAdd(championId, id =>
                new Lazy<Task<List<Skin>>>(
                    () => LoadChampionSkinsAsync(id),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                var skins = await lazy.Value.ConfigureAwait(false);
                if (skins is null)
                {
                    _skinLoadsByChampion.TryRemove(
                        new KeyValuePair<int, Lazy<Task<List<Skin>>>>(
                            championId, lazy));
                    return [];
                }

                return skins;
            }
            catch
            {
                _skinLoadsByChampion.TryRemove(
                    new KeyValuePair<int, Lazy<Task<List<Skin>>>>(
                        championId, lazy));
                throw;
            }
        }

        private async Task<List<Skin>> LoadChampionSkinsAsync(int championId)
        {
            var champion = await _httpService.GetAsync<ChampionSkins>(
                    $"lol-game-data/assets/v1/champions/{championId}.json")
                .ConfigureAwait(false);
            if (champion is null)
            {
                return null;
            }

            return champion.Skins ?? [];
        }
    }
}
