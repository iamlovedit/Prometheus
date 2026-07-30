using Moq;
using Prism.Ioc;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class GameResourceManagerTests
    {
        [Fact]
        public async Task GetPerkIconByIdAsync_WhenPerkIdIsInvalid_ReturnsNull()
        {
            var httpService = new Mock<IHttpService>();
            var container = new Mock<IContainerExtension>();
            var manager = new GameResourceManager(httpService.Object, container.Object);

            var result = await manager.GetPerkIconByIdAsync(0);

            Assert.Null(result);
            httpService.VerifyNoOtherCalls();
            container.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetPerkIconByIdAsync_WhenPerkMetadataIsUnavailable_ReturnsNull()
        {
            using var directory = new TemporaryDirectory();
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<List<Perk>>(
                    "lol-game-data/assets/v1/perks.json", null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<Perk>)null);
            var manager = new GameResourceManager(httpService.Object,
                CreateContainer(directory.Path).Object);

            var result = await manager.GetPerkIconByIdAsync(8005);

            Assert.Null(result);
            httpService.Verify(service => service.GetByteArrayResponseAsync(
                It.IsAny<HttpMethod>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetPerkIconByIdAsync_WhenPerkDoesNotExist_ReturnsNull()
        {
            using var directory = new TemporaryDirectory();
            var httpService = new Mock<IHttpService>();
            httpService.Setup(service => service.GetAsync<List<Perk>>(
                    "lol-game-data/assets/v1/perks.json", null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new Perk { Id = 8005, IconPath = "perk.png" }]);
            var manager = new GameResourceManager(httpService.Object,
                CreateContainer(directory.Path).Object);

            var result = await manager.GetPerkIconByIdAsync(9999);

            Assert.Null(result);
            httpService.Verify(service => service.GetByteArrayResponseAsync(
                It.IsAny<HttpMethod>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetPerkIconByIdAsync_WhenPerkExists_DownloadsAndCachesIcon()
        {
            using var directory = new TemporaryDirectory();
            var httpService = new Mock<IHttpService>();
            const string iconUrl = "lol-game-data/assets/perk.png";
            var iconBytes = new byte[] { 1, 2, 3, 4 };
            httpService.Setup(service => service.GetAsync<List<Perk>>(
                    "lol-game-data/assets/v1/perks.json", null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new Perk { Id = 8005, IconPath = iconUrl }]);
            httpService.Setup(service => service.GetByteArrayResponseAsync(
                    HttpMethod.Get, iconUrl, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(iconBytes);
            var manager = new GameResourceManager(httpService.Object,
                CreateContainer(directory.Path).Object);

            var result = await manager.GetPerkIconByIdAsync(8005);
            var cachedResult = await manager.GetPerkIconByIdAsync(8005);

            Assert.Equal(Path.Combine(directory.Path, "8005.png"), result);
            Assert.Equal(result, cachedResult);
            Assert.Equal(iconBytes, await File.ReadAllBytesAsync(result));
            httpService.Verify(service => service.GetByteArrayResponseAsync(
                HttpMethod.Get, iconUrl, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPerkIconByIdAsync_WhenDownloadFails_ReturnsNull()
        {
            using var directory = new TemporaryDirectory();
            var httpService = new Mock<IHttpService>();
            const string iconUrl = "lol-game-data/assets/perk.png";
            httpService.Setup(service => service.GetAsync<List<Perk>>(
                    "lol-game-data/assets/v1/perks.json", null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new Perk { Id = 8005, IconPath = iconUrl }]);
            httpService.Setup(service => service.GetByteArrayResponseAsync(
                    HttpMethod.Get, iconUrl, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("LCU unavailable"));
            var manager = new GameResourceManager(httpService.Object,
                CreateContainer(directory.Path).Object);

            var result = await manager.GetPerkIconByIdAsync(8005);

            Assert.Null(result);
            Assert.False(File.Exists(Path.Combine(directory.Path, "8005.png")));
        }

        private static Mock<IContainerExtension> CreateContainer(string directory)
        {
            var container = new Mock<IContainerExtension>();
            container.Setup(extension => extension.Resolve(typeof(string), ParameterNames.Perks))
                .Returns(directory);
            return container;
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "Prometheus.Tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
        }
    }
}
