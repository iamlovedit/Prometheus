using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Globalization;

namespace Prometheus.Services.Client
{
    public class GameService : IGameService
    {
        private static readonly TimeSpan[] LobbyConfirmationDelays =
        [
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(350)
        ];

        private const string _chatMe = "lol-chat/v1/me";
        private const string _checkUrl = "lol-matchmaking/v1/ready-check/";
        private const string _gameSessionUrl = "lol-champ-select/v1/session";
        private const string _gameActionUrl = "lol-champ-select/v1/session/actions/{0}";
        private const string _pickableChampionIds =
            "lol-champ-select/v1/pickable-champion-ids";
        private const string _legacyPickableChampions =
            "lol-champ-select/v1/pickable-champions";
        private const string _bannableChampionIds =
            "lol-champ-select/v1/bannable-champion-ids";
        private const string _legacyBannableChampions =
            "lol-champ-select/v1/bannable-champions";
        private const string _aramBenchSwapUrl =
            "lol-champ-select/v1/session/bench/swap/{0}";
        private const string _matchDetails = "lol-match-history/v1/games/{0}";
        private const string _champDataUrl = "https://x1-6833.native.qq.com/x1/6833/1061021&3af49f";
        private const string _gameSessionData = "lol-gameflow/v1/session";
        private const string _gameflowPhase = "lol-gameflow/v1/gameflow-phase";
        private const string _lobby = "lol-lobby/v2/lobby";
        private const string _matchmakingSearch = "lol-matchmaking/v1/search";
        private const string _postGame = "lol-end-of-game/v1/eog-stats-block";
        private const string _currentChampion = "lol-champ-select/v1/current-champion";
        private const string _champRestraintData = "https://lol.qq.com/act/lbp/common/guides/champDetail/champDetail_{0}.js";
        private const string _perks = "lol-perks/v1/pages";
        private const string _currentRune = "lol-perks/v1/currentpage";
        private const string _championskins = "lol-game-data/assets/v1/champions/{0}.json";
        private const string _profileIcons = "lol-game-data/assets/v1/profile-icons.json";
        private const string _spells = "lol-game-data/assets/v1/summoner-spells.json";
        private const string _items = "lol-game-data/assets/v1/items.json";
        private const string _backgroundSkin = "lol-summoner/v1/current-summoner/summoner-profile";
        private const string _setIcon = "lol-summoner/v1/current-summoner/icon";
        private const string _recommendPerks = "https://www.wegame.com.cn/lol/resources/js/champion/recommend/{0}.js";
        private const string LegacyManagedRunePageName = "Prometheus Recommended";
        private const string ManagedRunePageSuffix = " [Prometheus]";

        private readonly IHttpService _httpService;
        private readonly IClientService _clientService;
        private readonly SemaphoreSlim _matchmadeLobbyCreationGate = new(1, 1);

        public GameService(IHttpService httpService, IClientService clientService)
        {
            _httpService = httpService ?? throw new ArgumentNullException(nameof(httpService));
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
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

        public async Task<MatchDetail> GetMatchDetailAsync(
            long gameId,
            CancellationToken cancellationToken = default)
        {
            var match = await _httpService.GetAsync<MatchDetail>(
                string.Format(_matchDetails, gameId), null, cancellationToken)
                .ConfigureAwait(false);
            if (match is null)
            {
                return null;
            }

            var queues = await _clientService.GetQueuesAsync(cancellationToken)
                .ConfigureAwait(false);
            MatchGameModeResolver.Apply([match], queues);
            return match;
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
            await CompleteLegacyChampionSelectActionAsync(
                actionId, championId, "pick").ConfigureAwait(false);
        }

        public async Task<string> GetRuneItemsFromOnlineAsync(int championId)
        {
            return await _httpService.GetAsync(string.Format(_recommendPerks, championId));
        }

        public async Task<RuneRecommendationSet> GetRuneRecommendationsAsync(
            int championId,
            string assignedPosition,
            bool isAram,
            CancellationToken cancellationToken = default)
        {
            if (championId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(championId));
            }

            var lane = NormalizeLane(assignedPosition);
            if (!isAram)
            {
                var qqRecommendation = await TryGetQqRuneRecommendationsAsync(
                        championId, lane, cancellationToken)
                    .ConfigureAwait(false);
                if (qqRecommendation is not null)
                {
                    return qqRecommendation;
                }
            }

            return await TryGetWeGameRuneRecommendationsAsync(
                    championId, lane, isAram, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<RunePageApplyResult> ApplyRuneRecommendationAsync(
            string managedPageName,
            RuneRecommendationOption recommendation,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(managedPageName) ||
                !managedPageName.EndsWith(ManagedRunePageSuffix,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A Prometheus-managed rune page name is required.",
                    nameof(managedPageName));
            }

            if (!IsValidRecommendation(recommendation))
            {
                return new RunePageApplyResult
                {
                    Status = RunePageApplyStatus.InvalidRecommendation
                };
            }

            if (!_httpService.IsInitialized)
            {
                return new RunePageApplyResult
                {
                    Status = RunePageApplyStatus.ClientUnavailable
                };
            }

            var pagesResponse = await _httpService.GetAsync(
                    _perks, null, cancellationToken)
                .ConfigureAwait(false);
            var pages = DeserializeRunePages(pagesResponse);
            var existing = pages?.FirstOrDefault(page =>
                    page is not null &&
                    string.Equals(page.Name, managedPageName,
                        StringComparison.Ordinal)) ??
                pages?.FirstOrDefault(page =>
                    page is not null && IsManagedRunePageName(page.Name));
            var request = new LcuRunePage
            {
                Id = existing?.Id ?? 0,
                Name = managedPageName,
                Current = true,
                IsActive = true,
                IsDeletable = true,
                IsEditable = true,
                IsValid = true,
                PrimaryStyleId = recommendation.PrimaryStyleId,
                SubStyleId = recommendation.SubStyleId,
                SelectedPerkIds = recommendation.SelectedPerkIds.ToList()
            };

            var pageCreated = existing is null;
            string response;
            if (pageCreated)
            {
                response = await _httpService.PostAsync(
                        _perks, request, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                response = await _httpService.SendAsync(
                        HttpMethod.Put,
                        $"{_perks}/{existing.Id}",
                        request,
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var writtenPage = DeserializeRunePage(response);
            var currentPage = DeserializeRunePage(await _httpService.GetAsync(
                    _currentRune, null, cancellationToken)
                .ConfigureAwait(false));
            var pageId = currentPage?.Id ?? writtenPage?.Id ?? existing?.Id ?? 0;
            var pageCreatedConfirmed = pageCreated &&
                (string.Equals(writtenPage?.Name, managedPageName,
                     StringComparison.Ordinal) ||
                 string.Equals(currentPage?.Name, managedPageName,
                     StringComparison.Ordinal));
            return new RunePageApplyResult
            {
                Status = MatchesRecommendation(
                    currentPage, managedPageName, recommendation)
                    ? RunePageApplyStatus.Applied
                    : RunePageApplyStatus.ConfirmationFailed,
                RunePageId = pageId,
                PageCreated = pageCreatedConfirmed
            };
        }

        public async Task<string> GetPickableChampionsAsync()
        {
            return await _httpService.GetAsync(_pickableChampionIds);
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
                isCustom = true,
                queueId = GameQueueIds.PracticeTool
            };
            await _httpService.PostAsync(_lobby, body);
        }

        public async Task<MatchmadeLobbyCreationResult> CreateMatchmadeLobbyAsync(
            int queueId,
            CancellationToken cancellationToken = default)
        {
            if (queueId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queueId), queueId, "Queue id must be positive.");
            }

            if (!await _matchmadeLobbyCreationGate.WaitAsync(
                    TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false))
            {
                return CreateLobbyResult(
                    MatchmadeLobbyCreationStatus.OperationInProgress, queueId);
            }

            try
            {
                if (!_httpService.IsInitialized)
                {
                    return CreateLobbyResult(
                        MatchmadeLobbyCreationStatus.ClientUnavailable, queueId);
                }

                var queues = await _clientService.GetQueuesAsync(cancellationToken)
                    .ConfigureAwait(false);
                var queue = queues.FirstOrDefault(candidate => candidate.Id == queueId);
                if (queue is null || !queue.IsEnabled ||
                    !string.Equals(queue.QueueAvailability, "Available",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return CreateLobbyResult(
                        MatchmadeLobbyCreationStatus.QueueUnavailable, queueId);
                }

                var createdLobby = await _httpService.PostAsync<LobbySnapshot>(
                        _lobby,
                        new { queueId },
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (IsTargetLobby(createdLobby, queueId))
                {
                    return CreateLobbyResult(
                        MatchmadeLobbyCreationStatus.Created, queueId, createdLobby);
                }

                foreach (var delay in LobbyConfirmationDelays)
                {
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    LobbySnapshot lobby = null;
                    try
                    {
                        lobby = await GetLobbySnapshotAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (HttpRequestException exception) when (
                        exception.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                    }

                    if (IsTargetLobby(lobby, queueId))
                    {
                        return CreateLobbyResult(
                            MatchmadeLobbyCreationStatus.Created, queueId, lobby);
                    }
                }

                return CreateLobbyResult(
                    MatchmadeLobbyCreationStatus.LobbyNotConfirmed, queueId);
            }
            finally
            {
                _matchmadeLobbyCreationGate.Release();
            }
        }

        public async Task BanChampionAsync(int actionId, int championId)
        {
            await CompleteLegacyChampionSelectActionAsync(
                actionId, championId, "ban").ConfigureAwait(false);
        }

        public Task<IReadOnlyList<int>> GetPickableChampionIdsAsync(
            CancellationToken cancellationToken = default)
        {
            return GetChampionIdsAsync(
                _pickableChampionIds,
                _legacyPickableChampions,
                cancellationToken);
        }

        public Task<IReadOnlyList<int>> GetBannableChampionIdsAsync(
            CancellationToken cancellationToken = default)
        {
            return GetChampionIdsAsync(
                _bannableChampionIds,
                _legacyBannableChampions,
                cancellationToken);
        }

        public async Task CompleteChampionSelectActionAsync(
            ChampionSelectActionSnapshot action,
            int championId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (action.Id <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(action), action.Id, "Champion-select action id must be positive.");
            }

            if (championId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(championId), championId, "Champion id must be positive.");
            }

            if (!string.Equals(action.Type, "pick", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action.Type, "ban", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Champion-select action type must be pick or ban.", nameof(action));
            }

            var body = new
            {
                id = action.Id,
                actorCellId = action.ActorCellId,
                championId,
                type = action.Type,
                completed = false,
                isAllyAction = action.IsAllyAction,
                isInProgress = action.IsInProgress,
                pickTurn = action.PickTurn,
                duration = action.Duration
            };
            var url = string.Format(_gameActionUrl, action.Id);
            await _httpService.SendAsync(
                    HttpMethod.Patch, url, body, null, cancellationToken)
                .ConfigureAwait(false);
            await _httpService.PostAsync(
                    $"{url}/complete", null, cancellationToken)
                .ConfigureAwait(false);
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

        public Task<GameflowSessionSnapshot> GetGameflowSessionSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            return _httpService.GetAsync<GameflowSessionSnapshot>(
                _gameSessionData, null, cancellationToken);
        }

        public async Task<string> GetGameflowPhaseAsync(CancellationToken cancellationToken = default)
        {
            var json = await _httpService.GetAsync(
                _gameflowPhase, null, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            try
            {
                return JsonConvert.DeserializeObject<string>(json) ?? string.Empty;
            }
            catch (JsonException)
            {
                return json.Trim().Trim('"');
            }
        }

        public Task<LobbySnapshot> GetLobbySnapshotAsync(CancellationToken cancellationToken = default)
        {
            return _httpService.GetAsync<LobbySnapshot>(_lobby, null, cancellationToken);
        }

        public Task<MatchmakingSnapshot> GetMatchmakingSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            return _httpService.GetAsync<MatchmakingSnapshot>(
                _matchmakingSearch, null, cancellationToken);
        }

        public Task<ReadyCheckSnapshot> GetReadyCheckSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            return _httpService.GetAsync<ReadyCheckSnapshot>(_checkUrl, null, cancellationToken);
        }

        public async Task<ChampionSelectSnapshot> GetChampionSelectSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await _httpService.GetAsync<ChampionSelectSnapshot>(
                _gameSessionUrl, null, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                result.Actions ??= [];
                result.MyTeam ??= [];
                result.TheirTeam ??= [];
                result.BenchChampions ??= [];
            }

            return result;
        }

        public Task SwapAramBenchChampionAsync(
            int championId,
            CancellationToken cancellationToken = default)
        {
            if (championId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(championId), championId, "Champion id must be positive.");
            }

            return _httpService.PostAsync(
                string.Format(_aramBenchSwapUrl, championId),
                null,
                cancellationToken);
        }

        public Task<PostGameSnapshot> GetPostGameSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            return _httpService.GetAsync<PostGameSnapshot>(_postGame, null, cancellationToken);
        }

        public Task AcceptMatchAsync(CancellationToken cancellationToken)
        {
            return _httpService.PostAsync($"{_checkUrl}accept", null, cancellationToken);
        }

        public Task ReconnectGameAsync(CancellationToken cancellationToken)
        {
            return _httpService.PostAsync("lol-gameflow/v1/reconnect", null, cancellationToken);
        }

        private async Task<IReadOnlyList<int>> GetChampionIdsAsync(
            string endpoint,
            string legacyEndpoint,
            CancellationToken cancellationToken)
        {
            List<int> championIds = null;
            try
            {
                championIds = await _httpService.GetAsync<List<int>>(
                        endpoint, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (
                exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
            }

            if (championIds is null)
            {
                championIds = await _httpService.GetAsync<List<int>>(
                        legacyEndpoint, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            return championIds?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
        }

        private async Task CompleteLegacyChampionSelectActionAsync(
            int actionId,
            int championId,
            string type)
        {
            if (actionId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(actionId), actionId, "Champion-select action id must be positive.");
            }

            if (championId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(championId), championId, "Champion id must be positive.");
            }

            var url = string.Format(_gameActionUrl, actionId);
            await _httpService.SendAsync(
                    HttpMethod.Patch,
                    url,
                    new { type, championId })
                .ConfigureAwait(false);
            await _httpService.PostAsync($"{url}/complete", null).ConfigureAwait(false);
        }

        private async Task<RuneRecommendationSet> TryGetQqRuneRecommendationsAsync(
            int championId,
            string lane,
            CancellationToken cancellationToken)
        {
            try
            {
                var payload = await _httpService.GetAsync(
                        string.Format(_champRestraintData, championId),
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
                return ParseQqRuneRecommendations(payload, championId, lane);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecommendationDataException(exception))
            {
                Log.Warning(exception,
                    "Unable to load QQ rune recommendations for champion {ChampionId}",
                    championId);
                return null;
            }
        }

        private async Task<RuneRecommendationSet> TryGetWeGameRuneRecommendationsAsync(
            int championId,
            string lane,
            bool isAram,
            CancellationToken cancellationToken)
        {
            try
            {
                var payload = await _httpService.GetAsync(
                        string.Format(_recommendPerks, championId),
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
                return ParseWeGameRuneRecommendations(
                    payload, championId, lane, isAram);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecommendationDataException(exception))
            {
                Log.Warning(exception,
                    "Unable to load WeGame rune recommendations for champion {ChampionId}",
                    championId);
                return null;
            }
        }

        private static RuneRecommendationSet ParseQqRuneRecommendations(
            string payload,
            int championId,
            string preferredLane)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var root = JObject.Parse(UnwrapJavascriptObject(payload));
            var lanes = root.SelectToken("list.championLane") as JObject;
            if (lanes is null)
            {
                return null;
            }

            var laneValues = lanes.Properties()
                .Select(property => new
                {
                    Lane = NormalizeLane(property.Name),
                    Value = property.Value as JObject
                })
                .Where(value => value.Value is not null &&
                    !string.IsNullOrWhiteSpace(value.Value.Value<string>("perkdetail")))
                .ToArray();
            var selectedLane = laneValues.FirstOrDefault(value =>
                    string.Equals(value.Lane, preferredLane,
                        StringComparison.OrdinalIgnoreCase)) ??
                laneValues.OrderByDescending(value =>
                    ReadLong(value.Value["igamecnt"]))
                .FirstOrDefault();
            if (selectedLane is null)
            {
                return null;
            }

            var stylePairs = ParseEmbeddedObject(
                selectedLane.Value.Value<string>("mainviceperk"));
            var details = ParseEmbeddedObject(
                selectedLane.Value.Value<string>("perkdetail"));
            if (stylePairs is null || details is null)
            {
                return null;
            }

            var candidates = new List<RuneCandidate>();
            foreach (var group in details.Properties())
            {
                if (group.Value is not JObject groupRecommendations ||
                    stylePairs[group.Name] is not JObject stylePair)
                {
                    continue;
                }

                var primaryStyleId = GetStyleId(
                    stylePair.Value<string>("mainname"));
                var subStyleId = GetStyleId(
                    stylePair.Value<string>("viceperk"));
                if (primaryStyleId <= 0 || subStyleId <= 0)
                {
                    continue;
                }

                foreach (var item in groupRecommendations.Properties())
                {
                    if (item.Value is not JObject recommendation)
                    {
                        continue;
                    }

                    var perkIds = ParsePerkIds(
                        recommendation.Value<string>("perk"), '&');
                    if (perkIds.Count != 9)
                    {
                        continue;
                    }

                    candidates.Add(new RuneCandidate
                    {
                        PrimaryStyleId = primaryStyleId,
                        SubStyleId = subStyleId,
                        SelectedPerkIds = perkIds,
                        SampleCount = ReadLong(recommendation["igamecnt"]),
                        PickRateBasisPoints = ReadInt(recommendation["showrate"]),
                        WinRateBasisPoints = ReadInt(recommendation["winrate"])
                    });
                }
            }

            return CreateRecommendationSet(
                championId,
                selectedLane.Lane,
                "QQ",
                root.Value<string>("gameVer"),
                ParseDate(root.Value<string>("date")),
                candidates);
        }

        private static RuneRecommendationSet ParseWeGameRuneRecommendations(
            string payload,
            int championId,
            string preferredLane,
            bool isAram)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var root = JObject.Parse(payload);
            var values = (root["perk"] as JArray)?
                .OfType<JObject>()
                .Select(value => new
                {
                    Lane = NormalizeLane(value.Value<string>("lane")),
                    Value = value
                })
                .Where(value => isAram
                    ? string.Equals(value.Lane, "aram", StringComparison.Ordinal)
                    : !string.Equals(value.Lane, "aram", StringComparison.Ordinal))
                .ToArray() ?? [];
            if (values.Length == 0)
            {
                return null;
            }

            var selectedLane = isAram
                ? "aram"
                : values.Any(value => string.Equals(value.Lane, preferredLane,
                    StringComparison.OrdinalIgnoreCase))
                    ? preferredLane
                    : values.GroupBy(value => value.Lane)
                        .OrderByDescending(group => group.Max(value =>
                            ReadInt(value.Value["showrate"])))
                        .Select(group => group.Key)
                        .FirstOrDefault();
            var laneValues = values.Where(value =>
                    string.Equals(value.Lane, selectedLane,
                        StringComparison.OrdinalIgnoreCase))
                .Select(value => value.Value)
                .ToArray();
            var candidates = laneValues.Select(value => new RuneCandidate
            {
                PrimaryStyleId = value.Value<int>("primaryStyleId"),
                SubStyleId = value.Value<int>("subStyleId"),
                SelectedPerkIds = value["selectedPerkIds"]?
                    .Values<int>()
                    .Where(perkId => perkId > 0)
                    .ToArray() ?? [],
                PickRateBasisPoints = ReadInt(value["showrate"]),
                WinRateBasisPoints = ReadInt(value["winrate"])
            })
                .Where(candidate => candidate.PrimaryStyleId > 0 &&
                    candidate.SubStyleId > 0 &&
                    candidate.SelectedPerkIds.Count == 9)
                .ToArray();
            var updatedAt = laneValues
                .Select(value => ParseDate(value.Value<string>("update_time")))
                .Where(value => value.HasValue)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            return CreateRecommendationSet(
                championId,
                selectedLane,
                "WeGame",
                updatedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                updatedAt,
                candidates);
        }

        private static RuneRecommendationSet CreateRecommendationSet(
            int championId,
            string lane,
            string source,
            string dataVersion,
            DateTimeOffset? updatedAt,
            IReadOnlyCollection<RuneCandidate> candidates)
        {
            if (candidates is null || candidates.Count == 0)
            {
                return null;
            }

            var popular = candidates
                .OrderByDescending(candidate => candidate.SampleCount)
                .ThenByDescending(candidate => candidate.PickRateBasisPoints)
                .First();
            var minimumSample = popular.SampleCount > 0
                ? Math.Max(20, popular.SampleCount / 20)
                : 0;
            var minimumPickRate = popular.PickRateBasisPoints > 0
                ? Math.Max(10, popular.PickRateBasisPoints / 20)
                : 0;
            var reliableCandidates = candidates.Where(candidate =>
                    (candidate.SampleCount <= 0 ||
                        candidate.SampleCount >= minimumSample) &&
                    (candidate.PickRateBasisPoints <= 0 ||
                        candidate.PickRateBasisPoints >= minimumPickRate))
                .ToArray();
            var winRate = reliableCandidates
                .OrderByDescending(candidate => candidate.WinRateBasisPoints)
                .ThenByDescending(candidate => candidate.SampleCount)
                .ThenByDescending(candidate => candidate.PickRateBasisPoints)
                .FirstOrDefault() ?? popular;

            return new RuneRecommendationSet
            {
                ChampionId = championId,
                Lane = lane ?? string.Empty,
                Source = source ?? string.Empty,
                DataVersion = dataVersion ?? string.Empty,
                UpdatedAt = updatedAt,
                Popular = popular.ToOption(RuneRecommendationKind.Popular),
                WinRate = winRate.ToOption(RuneRecommendationKind.WinRate)
            };
        }

        private static bool IsRecommendationDataException(Exception exception)
        {
            return exception is HttpRequestException or JsonException or FormatException;
        }

        private static string UnwrapJavascriptObject(string payload)
        {
            var start = payload.IndexOf('{');
            var end = payload.LastIndexOf('}');
            if (start < 0 || end < start)
            {
                throw new JsonReaderException("Recommendation payload does not contain JSON.");
            }

            return payload.Substring(start, end - start + 1);
        }

        private static JObject ParseEmbeddedObject(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : JObject.Parse(value);
        }

        private static IReadOnlyList<int> ParsePerkIds(string value, char separator)
        {
            return value?.Split(separator, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var perkId) ? perkId : 0)
                .Where(perkId => perkId > 0)
                .ToArray() ?? [];
        }

        private static string NormalizeLane(string lane)
        {
            return lane?.Trim().ToLowerInvariant() switch
            {
                "middle" => "mid",
                "utility" => "support",
                "bot" or "adc" => "bottom",
                "top" => "top",
                "jungle" => "jungle",
                "mid" => "mid",
                "bottom" => "bottom",
                "support" => "support",
                "aram" => "aram",
                _ => string.Empty
            };
        }

        private static int GetStyleId(string styleName)
        {
            return styleName?.Trim() switch
            {
                "精密" => 8000,
                "主宰" => 8100,
                "巫术" => 8200,
                "启迪" => 8300,
                "坚决" => 8400,
                _ => 0
            };
        }

        private static int ReadInt(JToken token)
        {
            return int.TryParse(token?.ToString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static long ReadLong(JToken token)
        {
            return long.TryParse(token?.ToString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static DateTimeOffset? ParseDate(string value)
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var result) ? result : null;
        }

        private static bool IsValidRecommendation(RuneRecommendationOption recommendation)
        {
            return recommendation is not null &&
                recommendation.PrimaryStyleId > 0 &&
                recommendation.SubStyleId > 0 &&
                recommendation.SelectedPerkIds?.Count == 9 &&
                recommendation.SelectedPerkIds.All(perkId => perkId > 0);
        }

        private static LcuRunePage DeserializeRunePage(string response)
        {
            return string.IsNullOrWhiteSpace(response)
                ? null
                : JsonConvert.DeserializeObject<LcuRunePage>(response);
        }

        private static IReadOnlyList<LcuRunePage> DeserializeRunePages(string response)
        {
            return string.IsNullOrWhiteSpace(response)
                ? []
                : JsonConvert.DeserializeObject<List<LcuRunePage>>(response) ?? [];
        }

        private static bool MatchesRecommendation(
            LcuRunePage page,
            string managedPageName,
            RuneRecommendationOption recommendation)
        {
            return page is not null &&
                string.Equals(page.Name, managedPageName,
                    StringComparison.Ordinal) &&
                page.PrimaryStyleId == recommendation.PrimaryStyleId &&
                page.SubStyleId == recommendation.SubStyleId &&
                page.SelectedPerkIds?.SequenceEqual(
                    recommendation.SelectedPerkIds) == true;
        }

        private static bool IsManagedRunePageName(string pageName)
        {
            return string.Equals(pageName, LegacyManagedRunePageName,
                       StringComparison.Ordinal) ||
                   pageName?.EndsWith(ManagedRunePageSuffix,
                       StringComparison.Ordinal) == true;
        }

        private sealed class RuneCandidate
        {
            public int PrimaryStyleId { get; init; }

            public int SubStyleId { get; init; }

            public IReadOnlyList<int> SelectedPerkIds { get; init; } = [];

            public long SampleCount { get; init; }

            public int PickRateBasisPoints { get; init; }

            public int WinRateBasisPoints { get; init; }

            public RuneRecommendationOption ToOption(RuneRecommendationKind kind)
            {
                return new RuneRecommendationOption
                {
                    Kind = kind,
                    PrimaryStyleId = PrimaryStyleId,
                    SubStyleId = SubStyleId,
                    SelectedPerkIds = SelectedPerkIds.ToArray(),
                    SampleCount = SampleCount,
                    PickRateBasisPoints = PickRateBasisPoints,
                    WinRateBasisPoints = WinRateBasisPoints
                };
            }
        }

        private sealed class LcuRunePage
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("current")]
            public bool Current { get; set; }

            [JsonProperty("isActive")]
            public bool IsActive { get; set; }

            [JsonProperty("isDeletable")]
            public bool IsDeletable { get; set; }

            [JsonProperty("isEditable")]
            public bool IsEditable { get; set; }

            [JsonProperty("isValid")]
            public bool IsValid { get; set; }

            [JsonProperty("primaryStyleId")]
            public int PrimaryStyleId { get; set; }

            [JsonProperty("subStyleId")]
            public int SubStyleId { get; set; }

            [JsonProperty("selectedPerkIds")]
            public List<int> SelectedPerkIds { get; set; } = [];
        }

        private static bool IsTargetLobby(LobbySnapshot lobby, int queueId)
        {
            return lobby?.GameConfig?.QueueId == queueId;
        }

        private static MatchmadeLobbyCreationResult CreateLobbyResult(
            MatchmadeLobbyCreationStatus status,
            int queueId,
            LobbySnapshot lobby = null)
        {
            return new MatchmadeLobbyCreationResult
            {
                Status = status,
                QueueId = queueId,
                Lobby = lobby
            };
        }
    }
}
