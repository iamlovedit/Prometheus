using Moq;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Models;
using Prometheus.Modules.Home.ViewModels;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class HomeViewModelTests
    {
        [Fact]
        public void ConnectedSnapshot_AfterOfflinePlaceholder_LoadsCurrentSummoner()
        {
            using var context = new TestContext();

            Assert.Equal("Waiting for summoner data", context.ViewModel.SummonerName);

            context.Publish(new LiveMatchSnapshot
            {
                ConnectionState = ConnectionState.Connected,
                GameflowPhase = GameflowPhase.None,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            context.SummonerService.Verify(service => service.GetCurrentSummoner(
                It.IsAny<CancellationToken>()), Times.Once);
            context.SummonerService.Verify(service => service.GetMatchesAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("Prometheus", context.ViewModel.SummonerName);
            Assert.Equal("#TST", context.ViewModel.SummonerTag);
            Assert.Equal("100", context.ViewModel.SummonerLevel);
            Assert.False(context.ViewModel.ShowSummaryCard);
        }

        private sealed class TestContext : IDisposable
        {
            private LiveMatchSnapshot _current = LiveMatchSnapshot.Empty;

            public TestContext()
            {
                MatchService.SetupGet(service => service.Current)
                    .Returns(() => _current);
                MatchService.SetupGet(service => service.AutomationSettings)
                    .Returns(AutomationSettings.Object);
                ResourceService.Setup(service => service.FindResource<string>(
                        It.IsAny<string>()))
                    .Returns((string key) => key == "HomePage.NoSummoner"
                        ? "Waiting for summoner data"
                        : key);
                SummonerService.Setup(service => service.GetCurrentSummoner(
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SummonerAccount
                    {
                        GameName = "Prometheus",
                        TagLine = "TST",
                        Puuid = "test-puuid",
                        ProfileIconId = 29,
                        SummonerLevel = 100
                    });
                SummonerService.Setup(service => service.GetRankStatsByPuuid(
                        "test-puuid", It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string)null);
                GameResourceManager.Setup(service => service.GetProfileIconByIdAsync(29))
                    .ReturnsAsync("profile-icon.png");
                GameResourceManager.Setup(service => service.GetBackgroundSkinId())
                    .ReturnsAsync((string)null);

                ViewModel = new HomeViewModel(
                    RegionManager.Object,
                    new EventAggregator(),
                    MatchService.Object,
                    SummonerService.Object,
                    GameResourceManager.Object,
                    ResourceService.Object,
                    ClientService.Object);
            }

            public Mock<IRegionManager> RegionManager { get; } = new();

            public Mock<IMatchService> MatchService { get; } = new();

            public Mock<IGameAutomationSettings> AutomationSettings { get; } = new();

            public Mock<ISummonerService> SummonerService { get; } = new();

            public Mock<IGameResourceManager> GameResourceManager { get; } = new();

            public Mock<IResourceService> ResourceService { get; } = new();

            public Mock<IClientService> ClientService { get; } = new();

            public HomeViewModel ViewModel { get; }

            public void Publish(LiveMatchSnapshot snapshot)
            {
                _current = snapshot;
                MatchService.Raise(service => service.SnapshotChanged += null,
                    new LiveMatchSnapshotChangedEventArgs(snapshot));
            }

            public void Dispose()
            {
                ViewModel.Destroy();
            }
        }
    }
}
