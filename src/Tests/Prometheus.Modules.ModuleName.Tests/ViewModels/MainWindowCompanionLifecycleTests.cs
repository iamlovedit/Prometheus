using Moq;
using Prism.Events;
using Prism.Modularity;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Desktop.Services;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;
using Prometheus.ViewModels;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class MainWindowCompanionLifecycleTests
    {
        [Fact]
        public void LoadedAndWindowClosing_ControlCompanionLifecycle()
        {
            var eventAggregator = new EventAggregator();
            var matchService = new Mock<IMatchService>();
            var companion = new Mock<ILcuCompanionWindowController>();
            var resourceService = new Mock<IResourceService>();
            var quickMatchSettings = new Mock<IQuickMatchSettings>();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);
            matchService.Setup(service => service.StartAsync(
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            matchService.Setup(service => service.StopAsync())
                .Returns(Task.CompletedTask);
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string key) => key);
            quickMatchSettings.SetupGet(settings => settings.QueueId)
                .Returns(GameQueueIds.RankedSoloDuo);

            var viewModel = new MainWindowViewModel(
                new Mock<IRegionManager>().Object,
                eventAggregator,
                new Mock<IModuleManager>().Object,
                matchService.Object,
                new Mock<IClientService>().Object,
                new Mock<IClientListener>().Object,
                resourceService.Object,
                new Mock<IProfilePresentationStartupService>().Object,
                new Mock<IGameAutomationSettings>().Object,
                new Mock<IUpdateService>().Object,
                new Mock<IDialogService>().Object,
                new Mock<IGameService>().Object,
                quickMatchSettings.Object,
                companion.Object);

            viewModel.LoadedCommand.Execute();

            companion.Verify(service => service.Start(), Times.Once);

            eventAggregator.GetEvent<WindowClosingEvent>().Publish();

            companion.Verify(service => service.Stop(), Times.Once);
        }
    }
}
