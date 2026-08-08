using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Xunit;
using MatchModel = Prometheus.Core.Models.Match;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class SummonerServiceTests
    {
        private static SummonerService CreateService(
            Mock<IHttpService> httpService,
            Mock<IClientService> clientService = null)
        {
            if (clientService is null)
            {
                clientService = new Mock<IClientService>();
                clientService.Setup(service => service.GetQueuesAsync(
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<GameQueue>());
            }

            return new SummonerService(httpService.Object, clientService.Object);
        }

        [Fact]
        public async Task GetCurrentSummoner_WhenTokenProvided_ForwardsCancellationToken()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            var expected = new SummonerAccount { Puuid = "current-puuid" };
            httpService.Setup(service => service.GetAsync<SummonerAccount>(
                    "lol-summoner/v1/current-summoner", null, cancellationToken))
                .ReturnsAsync(expected);
            var service = CreateService(httpService);

            var result = await service.GetCurrentSummoner(cancellationToken);

            Assert.Same(expected, result);
            httpService.Verify(service => service.GetAsync<SummonerAccount>(
                "lol-summoner/v1/current-summoner", null, cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetCurrentSummoner_WhenLcuReturnsNotFound_ReturnsNull()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<SummonerAccount>(
                    "lol-summoner/v1/current-summoner", null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException(
                    "Current summoner unavailable", null, System.Net.HttpStatusCode.NotFound));
            var service = CreateService(httpService);

            var result = await service.GetCurrentSummoner();

            Assert.Null(result);
        }

        [Fact]
        public async Task GetCurrentSummoner_WhenResponseIsInvalid_ReturnsNull()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<SummonerAccount>(
                    "lol-summoner/v1/current-summoner", null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new JsonException("Invalid current summoner response"));
            var service = CreateService(httpService);

            var result = await service.GetCurrentSummoner();

            Assert.Null(result);
        }

        [Fact]
        public async Task SearchSummonerByName_WhenRiotIdProvided_UsesAliasEndpoint()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            var expected = new SummonerAccount { Puuid = "resolved-puuid" };
            httpService.Setup(service => service.PostAsync<List<SummonerAccount>>(
                    "lol-summoner/v1/summoners/aliases",
                    It.Is<object>(body => IsAliasRequest(body, "Visible Player", "CN1")),
                    null, cancellationToken))
                .ReturnsAsync([expected]);
            var service = CreateService(httpService);

            var result = await service.SearchSummonerByName(
                "  Visible Player＃CN1  ", cancellationToken);

            Assert.Same(expected, result);
            httpService.Verify(service => service.PostAsync<List<SummonerAccount>>(
                "lol-summoner/v1/summoners/aliases",
                It.Is<object>(body => IsAliasRequest(body, "Visible Player", "CN1")),
                null, cancellationToken), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Visible Player")]
        [InlineData("#CN1")]
        [InlineData("Visible Player#")]
        public async Task SearchSummonerByName_WhenRiotIdIsIncomplete_DoesNotCallLcu(
            string riotId)
        {
            var httpService = new Mock<IHttpService>();
            var service = CreateService(httpService);

            var result = await service.SearchSummonerByName(riotId);

            Assert.Null(result);
            httpService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SearchSummonerByName_WhenAliasRequestFails_ReturnsNull()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.PostAsync<List<SummonerAccount>>(
                    "lol-summoner/v1/summoners/aliases", It.IsAny<object>(), null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Summoner unavailable"));
            var service = CreateService(httpService);

            var result = await service.SearchSummonerByName("Visible Player#CN1");

            Assert.Null(result);
        }

        [Fact]
        public async Task SearchSummonerByPuuid_WhenTokenProvided_ForwardsCancellationToken()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            var expected = new SummonerAccount { Puuid = "player/puuid" };
            httpService.Setup(service => service.GetAsync<SummonerAccount>(
                    "lol-summoner/v2/summoners/puuid/player%2Fpuuid", null,
                    cancellationToken))
                .ReturnsAsync(expected);
            var service = CreateService(httpService);

            var result = await service.SearchSummonerByPuuid("player/puuid", cancellationToken);

            Assert.Same(expected, result);
            httpService.Verify(service => service.GetAsync<SummonerAccount>(
                "lol-summoner/v2/summoners/puuid/player%2Fpuuid", null,
                cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetRankStatsByPuuid_WhenTokenProvided_ForwardsCancellationToken()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            httpService.Setup(service => service.GetAsync(
                    "lol-ranked/v1/ranked-stats/player%2Fpuuid", null,
                    cancellationToken))
                .ReturnsAsync("ranked-stats");
            var service = CreateService(httpService);

            var result = await service.GetRankStatsByPuuid("player/puuid", cancellationToken);

            Assert.Equal("ranked-stats", result);
            httpService.Verify(service => service.GetAsync(
                "lol-ranked/v1/ranked-stats/player%2Fpuuid", null,
                cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetRankStatsByPuuid_WhenLcuRequestFails_ReturnsNull()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync(
                    "lol-ranked/v1/ranked-stats/player-puuid", null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Ranked stats unavailable"));
            var service = CreateService(httpService);

            var result = await service.GetRankStatsByPuuid("player-puuid");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetBackdorpByIdAsync_WhenLcuRequestFails_ReturnsNull()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync(
                    "lol-collections/v1/inventories/123/backdrop", null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Backdrop unavailable"));
            var service = CreateService(httpService);

            var result = await service.GetBackdorpByIdAsync(123);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetMatchHistoryAsync_RequestsStableTwentyMatchWindow()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            var stableWindow = Enumerable.Range(1, 25)
                .Select(gameId => new MatchModel { GameId = gameId })
                .ToList();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), cancellationToken))
                .ReturnsAsync(new MatchHistoryResponse
                {
                    Games = new MatchHistoryPage { Games = stableWindow }
                });
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryAsync(
                "test-puuid", cancellationToken);

            Assert.True(result.Succeeded);
            Assert.Equal(stableWindow.Take(20).Select(match => match.GameId),
                result.Matches.Select(match => match.GameId));
            httpService.Verify(service => service.GetAsync<MatchHistoryResponse>(
                "lol-match-history/v1/products/lol/test-puuid/matches",
                It.Is<IEnumerable<string>>(parameters => parameters.SequenceEqual(
                    new[] { "begIndex=0", "endIndex=19" })), cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetMatchHistoryAsync_WhenResponseHasNoGames_PreservesFailure()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryResponse());
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryAsync("test-puuid");

            Assert.False(result.Succeeded);
            Assert.Empty(result.Matches);
            Assert.NotEmpty(result.Error);
        }

        [Fact]
        public async Task GetMatchHistoryAsync_WhenHistoryIsEmpty_ReturnsSuccessfulResult()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryResponse
                {
                    Games = new MatchHistoryPage { Games = [] }
                });
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryAsync("test-puuid");

            Assert.True(result.Succeeded);
            Assert.Empty(result.Matches);
            Assert.Empty(result.Error);
        }

        [Fact]
        public async Task GetMatchHistoryAsync_WhenLcuRequestFails_PreservesFailure()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("LCU unavailable"));
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryAsync("test-puuid");

            Assert.False(result.Succeeded);
            Assert.Empty(result.Matches);
            Assert.NotEmpty(result.Error);
        }

        [Fact]
        public async Task GetMatchHistoryAsync_WhenPuuidIsMissing_DoesNotCallLcu()
        {
            var httpService = new Mock<IHttpService>();
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryAsync(string.Empty);

            Assert.False(result.Succeeded);
            Assert.Empty(result.Matches);
            httpService.VerifyNoOtherCalls();
        }

        [Fact]
        public void MatchHistoryResponse_DeserializesLcuGamesWrapper()
        {
            const string json = """
                {
                  "games": {
                    "games": [
                      {
                        "gameId": 98765,
                        "gameMode": "ARAM",
                        "gameType": "MATCHED_GAME",
                        "mapId": 12,
                        "queueId": 450,
                        "participants": [
                          {
                            "championId": 22,
                            "stats": {
                              "win": true,
                              "kills": 5,
                              "deaths": 2,
                              "assists": 7
                            }
                          }
                        ]
                      }
                    ]
                  }
                }
                """;

            var response = JsonConvert.DeserializeObject<MatchHistoryResponse>(json);

            var match = Assert.Single(response.Games.Games);
            Assert.Equal(98765, match.GameId);
            Assert.Equal("ARAM", match.GameMode);
            Assert.Equal("MATCHED_GAME", match.GameType);
            Assert.Equal(12, match.MapId);
            Assert.Equal(450, match.QueueId);
            var participant = Assert.Single(match.Participants);
            Assert.True(participant.Stats.Win);
            Assert.Equal(5, participant.Stats.Kills);
        }

        [Fact]
        public async Task GetMatchHistoryAsync_WhenQueueMetadataIsAvailable_UsesQueueShortName()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryResponse
                {
                    Games = new MatchHistoryPage
                    {
                        Games =
                        [
                            new MatchModel
                            {
                                GameId = 12345,
                                QueueId = 8888,
                                GameMode = "ARAM"
                            }
                        ]
                    }
                });
            var clientService = new Mock<IClientService>();
            clientService.Setup(service => service.GetQueuesAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new GameQueue
                    {
                        Id = 8888,
                        Name = "ARAM variant",
                        ShortName = "海克斯大乱斗",
                        GameMode = "ARAM"
                    }
                ]);
            var service = CreateService(httpService, clientService);

            var result = await service.GetMatchHistoryAsync("test-puuid");

            var match = Assert.Single(result.Matches);
            Assert.Equal("海克斯大乱斗", match.DisplayGameMode);
        }

        [Fact]
        public async Task GetMatchHistoryAsync_WhenQueueMetadataIsUnavailable_FallsBackToGameMode()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryResponse
                {
                    Games = new MatchHistoryPage
                    {
                        Games =
                        [
                            new MatchModel
                            {
                                GameId = 12345,
                                QueueId = 9999,
                                GameMode = "CLASSIC"
                            }
                        ]
                    }
                });
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryAsync("test-puuid");

            var match = Assert.Single(result.Matches);
            Assert.Equal("CLASSIC", match.DisplayGameMode);
        }

        private static bool IsAliasRequest(object body, string gameName, string tagLine)
        {
            var request = JArray.Parse(JsonConvert.SerializeObject(body)).First as JObject;
            return request?["gameName"]?.Value<string>() == gameName &&
                   request["tagLine"]?.Value<string>() == tagLine;
        }
    }
}
