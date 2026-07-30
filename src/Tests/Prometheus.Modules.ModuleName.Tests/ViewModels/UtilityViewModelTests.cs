using Moq;
using Prism.Regions;
using Prometheus.Core.Models;
using Prometheus.Modules.Utility.ViewModels;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class UtilityViewModelTests
    {
        [Fact]
        public void Constructor_LoadsSavedProfilePresentation()
        {
            var settings = new Mock<IProfilePresentationSettings>();
            settings.SetupGet(value => value.OnlineStatus).Returns("away");
            settings.SetupGet(value => value.StatusMessage).Returns("Hello");
            settings.SetupGet(value => value.QueueType)
                .Returns(QueueType.RANKED_FLEX_SR);
            settings.SetupGet(value => value.Tier).Returns(Tier.EMERALD);
            settings.SetupGet(value => value.Division).Returns(Division.II);

            var viewModel = CreateViewModel(settings: settings);

            Assert.Equal(1, viewModel.SelectedStatusIndex);
            Assert.Equal("Hello", viewModel.Status);
            Assert.Equal(2, viewModel.SelectedModeIndex);
            Assert.Equal(6, viewModel.SelectedTierIndex);
            Assert.Equal(1, viewModel.SelectedDivisionIndex);
        }

        [Fact]
        public async Task OnlineStatusChange_WhenClientUpdateSucceeds_SavesSelection()
        {
            var gameService = new Mock<IGameService>();
            var settings = new Mock<IProfilePresentationSettings>();
            var saved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            gameService.Setup(service => service.SetOnlineStatusAsync("offline"))
                .Returns(Task.CompletedTask);
            settings.Setup(value => value.SaveOnlineStatus("offline"))
                .Callback(() => saved.TrySetResult(true));
            var viewModel = CreateViewModel(gameService, settings);

            viewModel.SelectedStatusIndex = 2;

            await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            gameService.Verify(
                service => service.SetOnlineStatusAsync("offline"), Times.Once);
            settings.Verify(
                value => value.SaveOnlineStatus("offline"), Times.Once);
        }

        private static UtilityViewModel CreateViewModel(
            Mock<IGameService> gameService = null,
            Mock<IProfilePresentationSettings> settings = null)
        {
            return new UtilityViewModel(
                new Mock<IRegionManager>().Object,
                new Mock<IResourceService>().Object,
                (gameService ?? new Mock<IGameService>()).Object,
                (settings ?? new Mock<IProfilePresentationSettings>()).Object);
        }
    }
}
