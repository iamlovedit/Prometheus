using Moq;
using Prism.Events;
using Prism.Modularity;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;
using Prometheus.ViewModels;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class MainWindowViewModelTests
    {
        [Fact]
        public void LanguageSwitched_RefreshesDetachedTrayMenuText()
        {
            var language = "zh";
            var eventAggregator = new EventAggregator();
            var matchService = new Mock<IMatchService>();
            var resourceService = new Mock<IResourceService>();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string key) => $"{language}:{key}");

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
                new Mock<IDialogService>().Object);

            Assert.Equal("zh:Tray.ShowMainWindow", viewModel.TrayShowMainWindowText);
            Assert.Equal("zh:Tray.Exit", viewModel.TrayExitText);

            language = "en";
            eventAggregator.GetEvent<LanguageSwitchedEvent>().Publish();

            Assert.Equal("en:Tray.ShowMainWindow", viewModel.TrayShowMainWindowText);
            Assert.Equal("en:Tray.OpenMatch", viewModel.TrayOpenMatchText);
            Assert.Equal("en:HomePage.Action.Accept", viewModel.TrayAcceptText);
            Assert.Equal("en:Tray.Automation", viewModel.TrayAutomationText);
            Assert.Equal("en:Setting.Automation.AutoAccept", viewModel.TrayAutoAcceptText);
            Assert.Equal("en:Setting.Automation.AutoReconnect",
                viewModel.TrayAutoReconnectText);
            Assert.Equal("en:Menu.Setting", viewModel.TraySettingsText);
            Assert.Equal("en:Tray.Exit", viewModel.TrayExitText);
        }
    }
}
