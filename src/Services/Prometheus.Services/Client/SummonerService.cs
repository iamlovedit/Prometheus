using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Newtonsoft.Json;
using Serilog;

namespace Prometheus.Services.Client
{
    public class SummonerService : ISummonerService
    {
        private const string CurrentSummonerEndpoint = "lol-summoner/v1/current-summoner";
        private const string SummonerAliasesEndpoint = "lol-summoner/v1/summoners/aliases";
        private const string SummonerByPuuidEndpoint = "lol-summoner/v2/summoners/puuid/{0}";
        private const string RankedStatsEndpoint = "lol-ranked/v1/ranked-stats/{0}";
        private const string MatchHistoryEndpoint =
            "lol-match-history/v1/products/lol/{0}/matches";
        private const int MatchHistoryMatchCount = 20;
        private const string BackdropEndpoint =
            "lol-collections/v1/inventories/{0}/backdrop";

        private readonly IHttpService _httpService;
        private readonly IClientService _clientService;

        public SummonerService(IHttpService httpService, IClientService clientService)
        {
            _httpService = httpService ?? throw new ArgumentNullException(nameof(httpService));
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        }

        public async Task<string> GetBackdorpByIdAsync(long summonerId,
            CancellationToken cancellationToken = default)
        {
            if (summonerId <= 0)
            {
                return default;
            }

            try
            {
                return await _httpService.GetAsync(
                    string.Format(BackdropEndpoint, summonerId),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                Log.Error(exception, "Unable to load LCU summoner backdrop");
                return default;
            }
        }

        public async Task<SummonerAccount> GetCurrentSummoner(
            CancellationToken cancellationToken = default)
        {
            return await _httpService.GetAsync<SummonerAccount>(CurrentSummonerEndpoint,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> GetRankStatsByPuuid(string puuid,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(puuid))
            {
                return default;
            }

            try
            {
                return await _httpService.GetAsync(string.Format(
                    RankedStatsEndpoint, Uri.EscapeDataString(puuid)),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                Log.Error(exception, "Unable to load LCU ranked stats");
                return default;
            }
        }

        public async Task<SummonerAccount> SearchSummonerByName(string riotId,
            CancellationToken cancellationToken = default)
        {
            if (!TryParseRiotId(riotId, out var gameName, out var tagLine))
            {
                return default;
            }

            try
            {
                var aliases = await _httpService.PostAsync<List<SummonerAccount>>(
                    SummonerAliasesEndpoint,
                    new[]
                    {
                        new SummonerAliasRequest
                        {
                            GameName = gameName,
                            TagLine = tagLine
                        }
                    }, cancellationToken: cancellationToken).ConfigureAwait(false);
                return aliases?.FirstOrDefault();
            }
            catch (HttpRequestException exception)
            {
                Log.Error(exception, "Unable to search LCU summoner alias");
                return default;
            }
            catch (JsonException exception)
            {
                Log.Error(exception, "Unable to parse LCU summoner alias response");
                return default;
            }
        }

        public async Task<SummonerAccount> SearchSummonerByPuuid(string puuid,
            CancellationToken cancellationToken = default)
        {
            return await _httpService.GetAsync<SummonerAccount>(string.Format(
                SummonerByPuuidEndpoint, Uri.EscapeDataString(puuid)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MatchHistoryQueryResult> GetMatchHistoryAsync(string puuid,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(puuid))
            {
                return new MatchHistoryQueryResult
                {
                    Succeeded = false,
                    Error = "The match-history query is invalid."
                };
            }

            try
            {
                var response = await _httpService.GetAsync<MatchHistoryResponse>(
                    string.Format(MatchHistoryEndpoint, Uri.EscapeDataString(puuid)),
                    [
                        "begIndex=0",
                        $"endIndex={MatchHistoryMatchCount - 1}"
                    ], cancellationToken).ConfigureAwait(false);

                if (response?.Games is null)
                {
                    return new MatchHistoryQueryResult
                    {
                        Succeeded = false,
                        Error = "The match-history response is unavailable."
                    };
                }

                // The LCU caches the first response for this path while ignoring later pagination
                // parameters. Keep every caller on the same 20-match window; the Home dashboard
                // selects its first five matches after this method returns.
                var matches = (response.Games.Games ?? [])
                    .Take(MatchHistoryMatchCount)
                    .ToList();
                var queues = await _clientService.GetQueuesAsync(cancellationToken)
                    .ConfigureAwait(false);
                MatchGameModeResolver.Apply(matches, queues);

                return new MatchHistoryQueryResult
                {
                    Succeeded = true,
                    Matches = matches
                };
            }
            catch (HttpRequestException exception)
            {
                Log.Error(exception, "Unable to load LCU match history");
                return new MatchHistoryQueryResult
                {
                    Succeeded = false,
                    Error = "Unable to load match history."
                };
            }
            catch (JsonException exception)
            {
                Log.Error(exception, "Unable to parse LCU match history");
                return new MatchHistoryQueryResult
                {
                    Succeeded = false,
                    Error = "Unable to parse match history."
                };
            }
        }

        private static bool TryParseRiotId(string riotId, out string gameName,
            out string tagLine)
        {
            gameName = null;
            tagLine = null;

            var normalized = riotId?.Trim().Replace('＃', '#');
            var separatorIndex = normalized?.LastIndexOf('#') ?? -1;
            if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
            {
                return false;
            }

            gameName = normalized[..separatorIndex].Trim();
            tagLine = normalized[(separatorIndex + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(gameName) &&
                   !string.IsNullOrWhiteSpace(tagLine);
        }

        private sealed class SummonerAliasRequest
        {
            [JsonProperty("gameName")]
            public string GameName { get; init; }

            [JsonProperty("tagLine")]
            public string TagLine { get; init; }
        }
    }
}
