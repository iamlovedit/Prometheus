using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class GameServiceRuneRecommendationTests
    {
        private const string ManagedPageName =
            "Ahri - Most popular runes [Prometheus]";

        [Fact]
        public async Task GetRuneRecommendationsAsync_ForRift_UsesAssignedQqLane()
        {
            var httpService = new Mock<IHttpService>();
            var cancellationToken = new CancellationTokenSource().Token;
            httpService.Setup(service => service.GetAsync(
                    "https://lol.qq.com/act/lbp/common/guides/champDetail/champDetail_103.js",
                    null,
                    cancellationToken))
                .ReturnsAsync(CreateQqPayload());
            var service = CreateService(httpService);

            var result = await service.GetRuneRecommendationsAsync(
                103, "middle", false, cancellationToken);

            Assert.NotNull(result);
            Assert.Equal("mid", result.Lane);
            Assert.Equal("QQ", result.Source);
            Assert.Equal("16.15", result.DataVersion);
            Assert.Equal(8112, result.Popular.SelectedPerkIds[0]);
            Assert.Equal(8214, result.WinRate.SelectedPerkIds[0]);
            Assert.Equal(8100, result.Popular.PrimaryStyleId);
            Assert.Equal(8200, result.Popular.SubStyleId);
            httpService.Verify(service => service.GetAsync(
                It.Is<string>(url => url.Contains("wegame.com.cn", StringComparison.Ordinal)),
                null,
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetRuneRecommendationsAsync_ForAram_UsesWeGameAramOnly()
        {
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync(
                    "https://www.wegame.com.cn/lol/resources/js/champion/recommend/103.js",
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateWeGamePayload());
            var service = CreateService(httpService);

            var result = await service.GetRuneRecommendationsAsync(103, string.Empty, true);

            Assert.NotNull(result);
            Assert.Equal("aram", result.Lane);
            Assert.Equal("WeGame", result.Source);
            Assert.Equal(8128, result.Popular.SelectedPerkIds[0]);
            httpService.Verify(service => service.GetAsync(
                It.Is<string>(url => url.Contains("lol.qq.com", StringComparison.Ordinal)),
                null,
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ApplyRuneRecommendationAsync_CreatesAndConfirmsManagedPage()
        {
            var httpService = new Mock<IHttpService>();
            httpService.SetupGet(service => service.IsInitialized).Returns(true);
            httpService.Setup(service => service.GetAsync(
                    "lol-perks/v1/pages", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync("[]");
            httpService.Setup(service => service.PostAsync(
                    "lol-perks/v1/pages",
                    It.IsAny<object>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateRunePageJson(42));
            httpService.Setup(service => service.GetAsync(
                    "lol-perks/v1/currentpage", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateRunePageJson(42));
            var service = CreateService(httpService);
            var recommendation = CreateRecommendation();

            var result = await service.ApplyRuneRecommendationAsync(
                ManagedPageName, recommendation);

            Assert.True(result.Succeeded);
            Assert.True(result.PageCreated);
            Assert.Equal(42, result.RunePageId);
            httpService.Verify(service => service.PostAsync(
                "lol-perks/v1/pages",
                It.Is<object>(body => IsManagedRunePage(body)),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ApplyRuneRecommendationAsync_WhenClientUnavailable_DoesNotWrite()
        {
            var httpService = new Mock<IHttpService>();
            httpService.SetupGet(service => service.IsInitialized).Returns(false);
            var service = CreateService(httpService);

            var result = await service.ApplyRuneRecommendationAsync(
                ManagedPageName, CreateRecommendation());

            Assert.Equal(RunePageApplyStatus.ClientUnavailable, result.Status);
            httpService.Verify(service => service.PostAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            httpService.Verify(service => service.SendAsync(
                It.IsAny<HttpMethod>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ApplyRuneRecommendationAsync_WithManagedPage_UpdatesOnlyManagedPage()
        {
            var httpService = new Mock<IHttpService>();
            httpService.SetupGet(service => service.IsInitialized).Returns(true);
            httpService.Setup(service => service.GetAsync(
                    "lol-perks/v1/pages", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    [
                      {"id":7,"name":"Player page"},
                      {"id":42,"name":"Prometheus Recommended"}
                    ]
                    """);
            httpService.Setup(service => service.SendAsync(
                    HttpMethod.Put,
                    "lol-perks/v1/pages/42",
                    It.IsAny<object>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateRunePageJson(42));
            httpService.Setup(service => service.GetAsync(
                    "lol-perks/v1/currentpage", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateRunePageJson(42));
            var service = CreateService(httpService);

            var result = await service.ApplyRuneRecommendationAsync(
                ManagedPageName, CreateRecommendation());

            Assert.True(result.Succeeded);
            Assert.False(result.PageCreated);
            httpService.Verify(service => service.SendAsync(
                HttpMethod.Put,
                "lol-perks/v1/pages/42",
                It.Is<object>(body => IsManagedRunePage(body)),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
            httpService.Verify(service => service.PostAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            httpService.Verify(service => service.SendAsync(
                HttpMethod.Delete,
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ApplyRuneRecommendationAsync_WithoutOwnershipMarker_RejectsName()
        {
            var httpService = new Mock<IHttpService>();
            httpService.SetupGet(service => service.IsInitialized).Returns(true);
            var service = CreateService(httpService);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ApplyRuneRecommendationAsync(
                    "Ahri - Most popular runes",
                    CreateRecommendation()));

            httpService.Verify(service => service.SendAsync(
                It.IsAny<HttpMethod>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private static GameService CreateService(Mock<IHttpService> httpService)
        {
            return new GameService(
                httpService.Object,
                new Mock<IClientService>().Object);
        }

        private static string CreateQqPayload()
        {
            var stylePairs = new JObject
            {
                ["1"] = new JObject
                {
                    ["mainname"] = "主宰",
                    ["viceperk"] = "巫术"
                },
                ["2"] = new JObject
                {
                    ["mainname"] = "巫术",
                    ["viceperk"] = "主宰"
                }
            };
            var details = new JObject
            {
                ["1"] = new JObject
                {
                    ["1"] = QqRecommendation(
                        "8112&8139&8140&8106&8210&8226&5005&5008&5001",
                        1000,
                        5000,
                        5000)
                },
                ["2"] = new JObject
                {
                    ["1"] = QqRecommendation(
                        "8214&8226&8210&8237&8106&8139&5005&5008&5001",
                        100,
                        500,
                        5500)
                }
            };
            var root = new JObject
            {
                ["list"] = new JObject
                {
                    ["championLane"] = new JObject
                    {
                        ["mid"] = new JObject
                        {
                            ["igamecnt"] = "2000",
                            ["mainviceperk"] = stylePairs.ToString(Formatting.None),
                            ["perkdetail"] = details.ToString(Formatting.None)
                        }
                    }
                },
                ["gameVer"] = "16.15",
                ["date"] = "2026-08-08 20:15:08"
            };
            return $"var CHAMPION_DETAIL_103={root.ToString(Formatting.None)};/* test */";
        }

        private static JObject QqRecommendation(
            string perks,
            int games,
            int showRate,
            int winRate)
        {
            return new JObject
            {
                ["perk"] = perks,
                ["igamecnt"] = games,
                ["showrate"] = showRate,
                ["winrate"] = winRate
            };
        }

        private static string CreateWeGamePayload()
        {
            var root = new JObject
            {
                ["perk"] = new JArray
                {
                    WeGameRecommendation(
                        "aram",
                        8128,
                        "2025-01-15 14:14:31",
                        400,
                        5200),
                    WeGameRecommendation(
                        "mid",
                        8112,
                        "2025-01-15 14:14:31",
                        900,
                        5100)
                }
            };
            return root.ToString(Formatting.None);
        }

        private static JObject WeGameRecommendation(
            string lane,
            int keystone,
            string updatedAt,
            int showRate,
            int winRate)
        {
            return new JObject
            {
                ["lane"] = lane,
                ["primaryStyleId"] = 8100,
                ["subStyleId"] = 8200,
                ["selectedPerkIds"] = new JArray
                {
                    keystone, 8143, 8140, 8106, 8224, 8210, 5008, 5008, 5001
                },
                ["showrate"] = showRate,
                ["winrate"] = winRate,
                ["update_time"] = updatedAt
            };
        }

        private static RuneRecommendationOption CreateRecommendation()
        {
            return new RuneRecommendationOption
            {
                PrimaryStyleId = 8100,
                SubStyleId = 8200,
                SelectedPerkIds =
                [
                    8112, 8139, 8140, 8106, 8210, 8226, 5005, 5008, 5001
                ]
            };
        }

        private static string CreateRunePageJson(long id)
        {
            return new JObject
            {
                ["id"] = id,
                ["name"] = ManagedPageName,
                ["current"] = true,
                ["primaryStyleId"] = 8100,
                ["subStyleId"] = 8200,
                ["selectedPerkIds"] = new JArray
                {
                    8112, 8139, 8140, 8106, 8210, 8226, 5005, 5008, 5001
                }
            }.ToString(Formatting.None);
        }

        private static bool IsManagedRunePage(object body)
        {
            var page = JObject.Parse(JsonConvert.SerializeObject(body));
            return page.Value<string>("name") == ManagedPageName &&
                page.Value<bool>("current") &&
                page.Value<int>("primaryStyleId") == 8100 &&
                page["selectedPerkIds"]?.Count() == 9;
        }
    }
}
