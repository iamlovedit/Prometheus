using Moq;
using Prism.Events;
using Prism.Modularity;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Modules.Setting.ViewModels;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;
using Prometheus.ViewModels;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class UpdatePresentationViewModelTests
    {
        [Fact]
        public void SettingsAction_ChangesFromCheckToGitHubDownloadWhenUpdateIsAvailable()
        {
            AvailableUpdate availableUpdate = null;
            var state = UpdateState.Idle;
            var updateService = new Mock<IUpdateService>();
            updateService.SetupGet(service => service.State).Returns(() => state);
            updateService.SetupGet(service => service.AvailableUpdate)
                .Returns(() => availableUpdate);
            var preferenceViewModel = CreatePreferenceViewModel(updateService.Object);

            try
            {
                Assert.Equal("Update.Settings.Check", preferenceViewModel.UpdateActionText);

                availableUpdate = new AvailableUpdate();
                state = UpdateState.Available;
                RaiseUpdateStateChanged(updateService, state, 0);

                Assert.Equal("Update.Settings.Download", preferenceViewModel.UpdateActionText);
            }
            finally
            {
                preferenceViewModel.Destroy();
            }
        }

        [Fact]
        public void SettingsAction_WhenUpdateIsKnown_OpensGitHubWithoutCheckingAgain()
        {
            var updateService = new Mock<IUpdateService>();
            updateService.SetupGet(service => service.State).Returns(UpdateState.Available);
            updateService.SetupGet(service => service.AvailableUpdate)
                .Returns(new AvailableUpdate());
            updateService.Setup(service => service.OpenReleasePage()).Returns(true);

            var preferenceViewModel = CreatePreferenceViewModel(updateService.Object);

            try
            {
                preferenceViewModel.CheckForUpdatesCommand.Execute();

                updateService.Verify(service => service.OpenReleasePage(), Times.Once);
                updateService.Verify(service => service.CheckAsync(
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                preferenceViewModel.Destroy();
            }
        }

        [Fact]
        public void SettingsAction_WhenCheckFindsUpdate_OpensGitHubReleasePage()
        {
            var updateService = new Mock<IUpdateService>();
            updateService.SetupGet(service => service.State).Returns(UpdateState.Idle);
            updateService.Setup(service => service.CheckAsync(true,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AvailableUpdate());
            updateService.Setup(service => service.OpenReleasePage()).Returns(true);
            var preferenceViewModel = CreatePreferenceViewModel(updateService.Object);

            try
            {
                preferenceViewModel.CheckForUpdatesCommand.Execute();

                updateService.Verify(service => service.CheckAsync(true,
                    It.IsAny<CancellationToken>()), Times.Once);
                updateService.Verify(service => service.OpenReleasePage(), Times.Once);
            }
            finally
            {
                preferenceViewModel.Destroy();
            }
        }

        [Fact]
        public async Task SettingsBadge_FollowsKnownAvailableUpdateAcrossStateChanges()
        {
            AvailableUpdate availableUpdate = null;
            var updateService = new Mock<IUpdateService>();
            updateService.SetupGet(service => service.AvailableUpdate)
                .Returns(() => availableUpdate);
            var eventAggregator = new EventAggregator();
            var viewModel = CreateMainWindowViewModel(
                updateService.Object, eventAggregator);

            Assert.False(viewModel.HasAvailableUpdate);

            availableUpdate = new AvailableUpdate();
            RaiseUpdateStateChanged(updateService, UpdateState.Available, 0);
            Assert.True(viewModel.HasAvailableUpdate);

            RaiseUpdateStateChanged(updateService, UpdateState.Failed, 0.5, "download failed");
            Assert.True(viewModel.HasAvailableUpdate);

            availableUpdate = null;
            RaiseUpdateStateChanged(updateService, UpdateState.UpToDate, 0);
            Assert.False(viewModel.HasAvailableUpdate);

            var shutdownContext = new ApplicationShutdownContext();
            eventAggregator.GetEvent<WindowClosingEvent>().Publish(shutdownContext);
            await shutdownContext.WaitForCompletionAsync();
        }

        private static PreferenceViewModel CreatePreferenceViewModel(
            IUpdateService updateService)
        {
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);
            var logHistory = new Mock<ILogHistoryService>();
            logHistory.SetupGet(service => service.Capacity).Returns(1000);
            logHistory.Setup(service => service.GetSnapshot())
                .Returns(Array.Empty<LogEntry>());
            var loggingControl = new Mock<ILoggingControlService>();

            return new PreferenceViewModel(
                new EventAggregator(),
                CreateResourceService().Object,
                new Mock<IGameAutomationSettings>().Object,
                matchService.Object,
                logHistory.Object,
                loggingControl.Object,
                updateService);
        }

        private static MainWindowViewModel CreateMainWindowViewModel(
            IUpdateService updateService, IEventAggregator eventAggregator)
        {
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);

            return new MainWindowViewModel(
                new Mock<IRegionManager>().Object,
                eventAggregator,
                new Mock<IModuleManager>().Object,
                matchService.Object,
                new Mock<IClientService>().Object,
                new Mock<IClientListener>().Object,
                CreateResourceService().Object,
                new Mock<IProfilePresentationStartupService>().Object,
                new Mock<IGameAutomationSettings>().Object,
                updateService,
                new Mock<IGameService>().Object,
                CreateQuickMatchSettings().Object,
                new Mock<ILcuCompanionSettings>().Object);
        }

        private static Mock<IQuickMatchSettings> CreateQuickMatchSettings()
        {
            var settings = new Mock<IQuickMatchSettings>();
            settings.SetupGet(value => value.QueueId)
                .Returns(GameQueueIds.RankedSoloDuo);
            settings.Setup(value => value.SaveQueueId(It.IsAny<int>()))
                .Returns(true);
            return settings;
        }

        private static Mock<IResourceService> CreateResourceService()
        {
            var resourceService = new Mock<IResourceService>();
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string key) => key);
            return resourceService;
        }

        private static void RaiseUpdateStateChanged(Mock<IUpdateService> updateService,
            UpdateState state, double progress, string errorMessage = null)
        {
            updateService.Raise(service => service.StateChanged += null,
                new UpdateStateChangedEventArgs(state, progress, errorMessage));
        }
    }
}
