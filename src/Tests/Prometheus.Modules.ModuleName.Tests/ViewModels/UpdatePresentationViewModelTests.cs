using Moq;
using Prism.Events;
using Prism.Modularity;
using Prism.Regions;
using Prism.Services.Dialogs;
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
        [Theory]
        [InlineData(UpdateState.Idle, false)]
        [InlineData(UpdateState.Checking, false)]
        [InlineData(UpdateState.UpToDate, false)]
        [InlineData(UpdateState.Available, false)]
        [InlineData(UpdateState.Downloading, true)]
        [InlineData(UpdateState.ReadyToInstall, true)]
        [InlineData(UpdateState.Installing, true)]
        [InlineData(UpdateState.Failed, false)]
        public void ProgressVisibility_OnlyShowsFromDownloadThroughInstall(
            UpdateState state, bool expectedVisible)
        {
            var updateService = CreateUpdateService(state);
            var preferenceViewModel = CreatePreferenceViewModel(updateService.Object);
            var dialogViewModel = new UpdateDialogViewModel(
                updateService.Object,
                new EventAggregator(),
                CreateResourceService().Object);

            try
            {
                Assert.Equal(expectedVisible, preferenceViewModel.IsUpdateProgressVisible);
                Assert.Equal(expectedVisible, dialogViewModel.IsProgressVisible);
            }
            finally
            {
                preferenceViewModel.Destroy();
                dialogViewModel.OnDialogClosed();
            }
        }

        [Fact]
        public void ProgressVisibility_RefreshesWhenUpdateStateChanges()
        {
            var state = UpdateState.Available;
            var updateService = new Mock<IUpdateService>();
            updateService.SetupGet(service => service.State).Returns(() => state);
            updateService.SetupGet(service => service.Progress).Returns(0.5);

            var preferenceViewModel = CreatePreferenceViewModel(updateService.Object);
            var dialogViewModel = new UpdateDialogViewModel(
                updateService.Object,
                new EventAggregator(),
                CreateResourceService().Object);

            try
            {
                state = UpdateState.Downloading;
                RaiseUpdateStateChanged(updateService, state, 0.5);

                Assert.True(preferenceViewModel.IsUpdateProgressVisible);
                Assert.True(dialogViewModel.IsProgressVisible);

                state = UpdateState.Failed;
                RaiseUpdateStateChanged(updateService, state, 0.5, "download failed");

                Assert.False(preferenceViewModel.IsUpdateProgressVisible);
                Assert.False(dialogViewModel.IsProgressVisible);
            }
            finally
            {
                preferenceViewModel.Destroy();
                dialogViewModel.OnDialogClosed();
            }
        }

        [Fact]
        public void SettingsBadge_FollowsKnownAvailableUpdateAcrossStateChanges()
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

            eventAggregator.GetEvent<WindowClosingEvent>().Publish();
        }

        private static Mock<IUpdateService> CreateUpdateService(UpdateState state)
        {
            var updateService = new Mock<IUpdateService>();
            updateService.SetupGet(service => service.State).Returns(state);
            updateService.SetupGet(service => service.Progress).Returns(0.5);
            return updateService;
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
                updateService,
                new Mock<IDialogService>().Object);
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
                new Mock<IDialogService>().Object,
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
