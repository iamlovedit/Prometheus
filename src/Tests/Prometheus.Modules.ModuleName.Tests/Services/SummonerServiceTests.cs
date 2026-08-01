using Moq;
using Newtonsoft.Json;
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
        public async Task GetHomeRecentMatchesAsync_RequestsFiveMatchRange()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            var expectedMatches = new List<MatchModel>
            {
                new() { GameId = 12345 }
            };
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), cancellationToken))
                .ReturnsAsync(new MatchHistoryResponse
                {
                    Games = new MatchHistoryPage { Games = expectedMatches }
                });
            var service = CreateService(httpService);

            var result = await service.GetHomeRecentMatchesAsync(
                "test-puuid", cancellationToken);

            Assert.Equal(expectedMatches.Select(match => match.GameId),
                result.Select(match => match.GameId));
            httpService.Verify(service => service.GetAsync<MatchHistoryResponse>(
                "lol-match-history/v1/products/lol/test-puuid/matches",
                It.Is<IEnumerable<string>>(parameters => parameters.SequenceEqual(
                    new[] { "begIndex=0", "endIndex=4" })), cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetMatchHistoryPageAsync_WhenSecondPageRequested_RequestsTwentyMatchRange()
        {
            var httpService = new Mock<IHttpService>();
            var expectedMatches = Enumerable.Range(21, 20)
                .Select(gameId => new MatchModel { GameId = gameId })
                .ToList();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryResponse
                {
                    Games = new MatchHistoryPage { Games = expectedMatches }
                });
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryPageAsync("test-puuid", 2);

            Assert.True(result.Succeeded);
            Assert.Equal(expectedMatches.Select(match => match.GameId),
                result.Matches.Select(match => match.GameId));
            httpService.Verify(service => service.GetAsync<MatchHistoryResponse>(
                "lol-match-history/v1/products/lol/test-puuid/matches",
                It.Is<IEnumerable<string>>(parameters => parameters.SequenceEqual(
                    new[] { "begIndex=20", "endIndex=39" })),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetMatchHistoryPageAsync_WhenTenthPageRequested_RequestsLastRange()
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

            var result = await service.GetMatchHistoryPageAsync("test-puuid", 10);

            Assert.True(result.Succeeded);
            httpService.Verify(service => service.GetAsync<MatchHistoryResponse>(
                "lol-match-history/v1/products/lol/test-puuid/matches",
                It.Is<IEnumerable<string>>(parameters => parameters.SequenceEqual(
                    new[] { "begIndex=180", "endIndex=199" })),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetMatchHistoryPageAsync_WhenPageIndexIsInvalid_DoesNotCallLcu()
        {
            var httpService = new Mock<IHttpService>();
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryPageAsync("test-puuid", 0);

            Assert.False(result.Succeeded);
            Assert.NotEmpty(result.Error);
            httpService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetHomeRecentMatchesAsync_WhenResponseHasNoGames_ReturnsEmptyList()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryResponse());
            var service = CreateService(httpService);

            var result = await service.GetHomeRecentMatchesAsync("test-puuid");

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetHomeRecentMatchesAsync_WhenLcuRequestFails_ReturnsEmptyList()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("LCU unavailable"));
            var service = CreateService(httpService);

            var result = await service.GetHomeRecentMatchesAsync("test-puuid");

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetMatchHistoryPageAsync_WhenHistoryIsEmpty_ReturnsSuccessfulResult()
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

            var result = await service.GetMatchHistoryPageAsync("test-puuid", 1);

            Assert.True(result.Succeeded);
            Assert.Empty(result.Matches);
            Assert.Empty(result.Error);
        }

        [Fact]
        public async Task GetMatchHistoryPageAsync_WhenLcuRequestFails_PreservesFailure()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("LCU unavailable"));
            var service = CreateService(httpService);

            var result = await service.GetMatchHistoryPageAsync("test-puuid", 1);

            Assert.False(result.Succeeded);
            Assert.Empty(result.Matches);
            Assert.NotEmpty(result.Error);
        }

        [Fact]
        public async Task GetHomeRecentMatchesAsync_WhenPuuidIsMissing_DoesNotCallLcu()
        {
            var httpService = new Mock<IHttpService>();
            var service = CreateService(httpService);

            var result = await service.GetHomeRecentMatchesAsync(string.Empty);

            Assert.Empty(result);
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
        public async Task GetMatchHistoryPageAsync_WhenQueueMetadataIsAvailable_UsesQueueShortName()
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

            var result = await service.GetMatchHistoryPageAsync("test-puuid", 1);

            var match = Assert.Single(result.Matches);
            Assert.Equal("海克斯大乱斗", match.DisplayGameMode);
        }

        [Fact]
        public async Task GetMatchHistoryPageAsync_WhenQueueMetadataIsUnavailable_FallsBackToGameMode()
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

            var result = await service.GetMatchHistoryPageAsync("test-puuid", 1);

            var match = Assert.Single(result.Matches);
            Assert.Equal("CLASSIC", match.DisplayGameMode);
        }
    }
}
