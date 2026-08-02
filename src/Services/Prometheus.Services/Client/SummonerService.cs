using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Newtonsoft.Json;
using Serilog;
using System.Web;

namespace Prometheus.Services.Client
{
    public class SummonerService : ISummonerService
    {
        private const string CurrentSummonerEndpoint = "lol-summoner/v1/current-summoner";
        private const string SummonerByNameEndpoint = "lol-summoner/v1/summoners";
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

        public async Task<SummonerAccount> SearchSummonerByName(string nickname)
        {
            return await _httpService.GetAsync<SummonerAccount>(SummonerByNameEndpoint,
            [
               $"name={HttpUtility.UrlEncode(nickname)}"
            ]);
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
    }
}
