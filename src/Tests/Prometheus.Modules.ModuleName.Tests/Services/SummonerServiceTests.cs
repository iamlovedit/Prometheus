using Moq;
using Newtonsoft.Json;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using MatchModel = Prometheus.Core.Models.Match;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class SummonerServiceTests
    {
        [Fact]
        public async Task GetCurrentSummoner_WhenTokenProvided_ForwardsCancellationToken()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            var expected = new SummonerAccount { Puuid = "current-puuid" };
            httpService.Setup(service => service.GetAsync<SummonerAccount>(
                    "lol-summoner/v1/current-summoner", null, cancellationToken))
                .ReturnsAsync(expected);
            var service = new SummonerService(httpService.Object);

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
            var service = new SummonerService(httpService.Object);

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
            var service = new SummonerService(httpService.Object);

            var result = await service.GetRankStatsByPuuid("player/puuid", cancellationToken);

            Assert.Equal("ranked-stats", result);
            httpService.Verify(service => service.GetAsync(
                "lol-ranked/v1/ranked-stats/player%2Fpuuid", null,
                cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetMatchesAsync_WhenResponseContainsGames_ReturnsRequestedPage()
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
            var service = new SummonerService(httpService.Object);

            var result = await service.GetMatchesAsync("test-puuid", 0, 19, cancellationToken);

            Assert.Same(expectedMatches, result);
            httpService.Verify(service => service.GetAsync<MatchHistoryResponse>(
                "lol-match-history/v1/products/lol/test-puuid/matches",
                It.Is<IEnumerable<string>>(parameters => parameters.SequenceEqual(
                    new[] { "begIndex=0", "endIndex=19" })), cancellationToken), Times.Once);
        }

        [Fact]
        public async Task GetMatchesAsync_WhenResponseHasNoGames_ReturnsEmptyList()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryResponse());
            var service = new SummonerService(httpService.Object);

            var result = await service.GetMatchesAsync("test-puuid", 0, 19);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetMatchesAsync_WhenLcuRequestFails_ReturnsEmptyList()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("LCU unavailable"));
            var service = new SummonerService(httpService.Object);

            var result = await service.GetMatchesAsync("test-puuid", 0, 19);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetMatchesResultAsync_WhenHistoryIsEmpty_ReturnsSuccessfulResult()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchHistoryResponse
                {
                    Games = new MatchHistoryPage { Games = [] }
                });
            var service = new SummonerService(httpService.Object);

            var result = await service.GetMatchesResultAsync("test-puuid", 0, 19);

            Assert.True(result.Succeeded);
            Assert.Empty(result.Matches);
            Assert.Empty(result.Error);
        }

        [Fact]
        public async Task GetMatchesResultAsync_WhenLcuRequestFails_PreservesFailure()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<MatchHistoryResponse>(
                    It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("LCU unavailable"));
            var service = new SummonerService(httpService.Object);

            var result = await service.GetMatchesResultAsync("test-puuid", 0, 19);

            Assert.False(result.Succeeded);
            Assert.Empty(result.Matches);
            Assert.NotEmpty(result.Error);
        }

        [Fact]
        public async Task GetMatchesAsync_WhenPuuidIsMissing_DoesNotCallLcu()
        {
            var httpService = new Mock<IHttpService>();
            var service = new SummonerService(httpService.Object);

            var result = await service.GetMatchesAsync(string.Empty, 0, 19);

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
            var participant = Assert.Single(match.Participants);
            Assert.True(participant.Stats.Win);
            Assert.Equal(5, participant.Stats.Kills);
        }
    }
}
