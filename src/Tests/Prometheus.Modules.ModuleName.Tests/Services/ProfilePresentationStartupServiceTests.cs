using Moq;
using Prometheus.Core.Models;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class ProfilePresentationStartupServiceTests
    {
        [Fact]
        public async Task FirstConnectedSnapshot_AppliesEverySavedValueOnce()
        {
            var matchService = new Mock<IMatchService>();
            var gameService = new Mock<IGameService>();
            var settings = new Mock<IProfilePresentationSettings>();
            var rankApplied = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            matchService.SetupGet(service => service.Current)
                .Returns(new LiveMatchSnapshot
                {
                    ConnectionState = ConnectionState.Connecting
                });
            settings.SetupGet(value => value.OnlineStatus).Returns("chat");
            settings.SetupGet(value => value.StatusMessage).Returns("Hello");
            settings.SetupGet(value => value.QueueType)
                .Returns(QueueType.RANKED_SOLO_5x5);
            settings.SetupGet(value => value.Tier).Returns(Tier.GOLD);
            settings.SetupGet(value => value.Division).Returns(Division.III);

            gameService.Setup(service => service.SetOnlineStatusAsync("chat"))
                .Returns(Task.CompletedTask);
            gameService.Setup(service => service.SetStatusAsync("Hello"))
                .Returns(Task.CompletedTask);
            gameService.Setup(service => service.SetChatTierAsync(
                    QueueType.RANKED_SOLO_5x5,
                    Tier.GOLD,
                    Division.III))
                .ReturnsAsync(string.Empty)
                .Callback(() => rankApplied.TrySetResult(true));

            var service = new ProfilePresentationStartupService(
                matchService.Object,
                gameService.Object,
                settings.Object);

            service.Start();
            RaiseConnected(matchService);
            await rankApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));
            RaiseConnected(matchService);

            gameService.Verify(
                value => value.SetOnlineStatusAsync("chat"), Times.Once);
            gameService.Verify(
                value => value.SetStatusAsync("Hello"), Times.Once);
            gameService.Verify(value => value.SetChatTierAsync(
                QueueType.RANKED_SOLO_5x5,
                Tier.GOLD,
                Division.III), Times.Once);

            service.Stop();
        }

        [Fact]
        public void UnconfiguredValues_AreNotApplied()
        {
            var matchService = new Mock<IMatchService>();
            var gameService = new Mock<IGameService>();
            var settings = new Mock<IProfilePresentationSettings>();

            matchService.SetupGet(service => service.Current)
                .Returns(new LiveMatchSnapshot
                {
                    ConnectionState = ConnectionState.Connected
                });

            var service = new ProfilePresentationStartupService(
                matchService.Object,
                gameService.Object,
                settings.Object);

            service.Start();

            gameService.Verify(
                value => value.SetOnlineStatusAsync(It.IsAny<string>()), Times.Never);
            gameService.Verify(
                value => value.SetStatusAsync(It.IsAny<string>()), Times.Never);
            gameService.Verify(value => value.SetChatTierAsync(
                It.IsAny<QueueType>(),
                It.IsAny<Tier>(),
                It.IsAny<Division>()), Times.Never);

            service.Stop();
        }

        private static void RaiseConnected(Mock<IMatchService> matchService)
        {
            matchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(new LiveMatchSnapshot
                {
                    ConnectionState = ConnectionState.Connected
                }));
        }
    }
}
