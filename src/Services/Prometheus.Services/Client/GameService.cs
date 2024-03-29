﻿using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Net.Http;
using System.Threading.Tasks;

namespace Prometheus.Services.Client
{
    public class GameService : IGameService
    {
        private const string _chatMe = "lol-chat/v1/me";
        private const string _checkUrl = "lol-matchmaking/v1/ready-check/";
        private const string _gameSessionUrl = "lol-champ-select/v1/session";
        private const string _gameActionUrl = "lol-champ-select/v1/session/actions/{0}";
        private const string _matchDetails = "lol-match-history/v1/games/{0}";
        private const string _champDataUrl = "https://x1-6833.native.qq.com/x1/6833/1061021&3af49f";
        private const string _gameSessionData = "lol-gameflow/v1/session";
        private const string _currentChampion = "/lol-champ-select/v1/current-champion";
        private const string _pickableChampion = "/lol-champ-select/v1/pickable-champions";
        private const string _champRestraintData = "https://lol.qq.com/act/lbp/common/guides/champDetail/champDetail_{0}.js?ts=2760378";
        private const string _perks = "lol-perks/v1/pages";
        private const string _currentRune = "lol-perks/v1/currentpage";
        private const string _championskins = "lol-game-data/assets/v1/champions/{0}.json";
        private const string _profileIcons = "lol-game-data/assets/v1/profile-icons.json";
        private const string _spells = "lol-game-data/assets/v1/summoner-spells.json";
        private const string _items = "lol-game-data/assets/v1/items.json";
        private const string _backgroundSkin = "lol-summoner/v1/current-summoner/summoner-profile";
        private const string _setIcon = "lol-summoner/v1/current-summoner/icon";
        private const string _recommendPerks = "https://www.wegame.com.cn/lol/resources/js/champion/recommend/{0}.js ";

        private readonly IHttpService _httpService;
        public GameService(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task CreateRunePage(object body)
        {
            await _httpService.SendAsync(HttpMethod.Post, _perks, body);
        }

        public async Task DeleteRunePage(long id)
        {
            await _httpService.SendAsync(HttpMethod.Delete, $"{_perks}/{id}", null);
        }

        public async Task<string> GetAllRunePages()
        {
            return await _httpService.GetAsync(_perks);
        }

        public Task<string> GetCurrentChampionInfoAsync()
        {
            return _httpService.GetAsync(_currentChampion);
        }

        public Task<string> GetCurrentGameInfoAsync()
        {
            return _httpService.GetAsync(_gameSessionData);
        }

        public async Task<string> GetCurrentRunePage()
        {
            return await _httpService.GetAsync(_currentRune);
        }

        public async Task<MatchDetail> GetMatchDetailAsync(long gameId)
        {
            return await _httpService.GetAsync<MatchDetail>(string.Format(_matchDetails, gameId), null);
        }

        public async Task<string> GetGameSessionAsync()
        {
            return await _httpService.GetAsync(_gameSessionUrl);
        }

        public async Task<string> GetProfileIcons()
        {
            return await _httpService.GetAsync(_profileIcons);
        }

        public async Task<string> GetItems()
        {
            return await _httpService.GetAsync(_items);
        }

        public async Task<byte[]> GetResourceByUrl(string url)
        {
            return await _httpService.GetByteArrayResponseAsync(HttpMethod.Get, url);
        }

        public Task<string> GetSpells()
        {
            return _httpService.GetAsync(_spells);
        }

        public async Task AcceptMatchAsync()
        {
            await _httpService.PostAsync($"{_checkUrl}accept", null);
        }

        public async Task PickChampionAsync(int actionId, int championId)
        {
            var body = new
            {
                type = "pick",
                championId
            };
            var url = string.Format(_gameActionUrl, actionId);
            await _httpService.SendAsync(HttpMethod.Patch, url, body);
        }

        public async Task<string> GetRuneItemsFromOnlineAsync(int championId)
        {
            return await _httpService.GetAsync(string.Format(_recommendPerks, championId));
        }

        public async Task<string> GetPickableChampionsAsync()
        {
            return await _httpService.GetAsync(_pickableChampion);
        }

        public async Task<string> GetChampionRankAsync(string lane, int tier, int time)
        {
            return await _httpService.GetAsync(_champDataUrl,
            [
                "championid=666",
                $"lane={lane}",
                $"dtstatdate={time}",
                $"tier={tier}",
                "ijob=all",
                "gamequeueconfigid=420"
            ]);
        }

        public async Task<string> SetSkinAsync(object body)
        {
            return await _httpService.PostAsync(_backgroundSkin, body, null);
        }

        public async Task<string> SetIconAsync(object body)
        {
            return await _httpService.SendAsync(HttpMethod.Put, _setIcon, body);
        }

        public async Task<string> GetChampionSkinById(int id)
        {
            return await _httpService.GetAsync(string.Format(_championskins, id));
        }

        public async Task CreatePracticeLobbyAsync(string name, string password)
        {
            var mutators = new
            {
                id = 1
            };
            var configuration = new
            {
                gameMode = "PRACTICETOOL",
                gameMutator = "",
                gameServerRegion = "",
                mapId = 11,
                mutators,
                spectatorPolicy = "AllAllowed",
                teamSize = 5
            };
            var customGameLobby = new
            {
                configuration,
                lobbyName = name,
                lobbyPassword = password
            };

            var body = new
            {
                customGameLobby,
                isCustom = true
            };
            await _httpService.PostAsync("lol-lobby/v2/lobby", body);
        }

        public async Task BanChampionAsync(int actionId, int championId)
        {
            var body = new
            {
                type = "pick",
                championId
            };
            var url = string.Format(_gameActionUrl, actionId);
            await _httpService.SendAsync(HttpMethod.Patch, url, body);
        }

        public async Task<string> SetChatTierAsync(QueueType queueType, Tier tier, Division division)
        {
            var lol = new
            {
                rankedLeagueQueue = queueType,
                rankedLeagueTier = tier,
                rankedLeagueDivision = division,
            };

            var body = new
            {
                lol
            };
            return await _httpService.SendAsync(HttpMethod.Put, _chatMe, body);
        }

        public async Task ReconnectGameAsync()
        {
            await _httpService.PostAsync("lol-gameflow/v1/reconnect", null);
        }

        public async Task SetOnlineStatusAsync(string chatStatus)
        {
            var body = new
            {
                availability = chatStatus
            };
            await _httpService.SendAsync(HttpMethod.Put, _chatMe, body);
        }

        public async Task SetStatusAsync(string status)
        {
            var body = new
            {
                statusMessage = status
            };
            await _httpService.SendAsync(HttpMethod.Put, _chatMe, body);
        }

        public async Task<string> GetAcceptStatusAsync()
        {
            return await _httpService.GetAsync(_checkUrl);
        }

        public async Task<string> GetMapSideAsync()
        {
            return await _httpService.GetAsync("lol-champ-select/v1/pin-drop-notification");
        }
    }
}
