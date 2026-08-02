using Moq;
using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class GameServiceChampionSelectTests
    {
        [Fact]
        public async Task CompleteChampionSelectActionAsync_PatchesThenCompletesAction()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.SendAsync(
                    HttpMethod.Patch,
                    "lol-champ-select/v1/session/actions/17",
                    It.IsAny<object>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(string.Empty);
            httpService.Setup(service => service.PostAsync(
                    "lol-champ-select/v1/session/actions/17/complete",
                    null,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var service = new GameService(
                httpService.Object,
                new Mock<IClientService>().Object);
            var action = new ChampionSelectActionSnapshot
            {
                Id = 17,
                ActorCellId = 3,
                Type = "ban",
                IsAllyAction = true,
                IsInProgress = true,
                PickTurn = 2
            };

            await service.CompleteChampionSelectActionAsync(action, 103);

            httpService.Verify(value => value.SendAsync(
                HttpMethod.Patch,
                "lol-champ-select/v1/session/actions/17",
                It.Is<object>(body =>
                    JObject.FromObject(body).Value<string>("type") == "ban" &&
                    JObject.FromObject(body).Value<int>("championId") == 103),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
            httpService.Verify(value => value.PostAsync(
                "lol-champ-select/v1/session/actions/17/complete",
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPickableChampionIdsAsync_NormalizesCurrentEndpointResponse()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<List<int>>(
                    "lol-champ-select/v1/pickable-champion-ids",
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([103, 22, 103, 0]);
            var service = new GameService(
                httpService.Object,
                new Mock<IClientService>().Object);

            var championIds = await service.GetPickableChampionIdsAsync();

            Assert.Equal([103, 22], championIds);
        }
    }
}
