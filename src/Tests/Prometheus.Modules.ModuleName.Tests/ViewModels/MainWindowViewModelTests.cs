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
using System.ComponentModel;
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
                new Mock<IDialogService>().Object,
                new Mock<IGameService>().Object,
                CreateQuickMatchSettings().Object,
                CreateCompanionSettings().Object);

            Assert.Equal("zh:Tray.ShowMainWindow", viewModel.TrayShowMainWindowText);
            Assert.Equal("zh:Tray.QuickMatch", viewModel.TrayQuickMatchText);
            Assert.Equal("zh:Tray.Exit", viewModel.TrayExitText);

            language = "en";
            eventAggregator.GetEvent<LanguageSwitchedEvent>().Publish();

            Assert.Equal("en:Tray.ShowMainWindow", viewModel.TrayShowMainWindowText);
            Assert.Equal("en:Tray.QuickMatch", viewModel.TrayQuickMatchText);
            Assert.Equal("en:HomePage.QuickMatch.SoloDuo",
                viewModel.TrayQuickMatchSoloDuoText);
            Assert.Equal("en:Tray.OpenMatch", viewModel.TrayOpenMatchText);
            Assert.Equal("en:HomePage.Action.Accept", viewModel.TrayAcceptText);
            Assert.Equal("en:Tray.Automation", viewModel.TrayAutomationText);
            Assert.Equal("en:Setting.Automation.AutoAccept", viewModel.TrayAutoAcceptText);
            Assert.Equal("en:Setting.Automation.AutoReconnect",
                viewModel.TrayAutoReconnectText);
            Assert.Equal("en:Utility.AramSwap", viewModel.TrayAramSwapText);
            Assert.Equal("en:Utility.Companion.Title", viewModel.TrayCompanionText);
            Assert.Equal("en:Menu.Setting", viewModel.TraySettingsText);
            Assert.Equal("en:Tray.Exit", viewModel.TrayExitText);
        }

        [Fact]
        public void AramSwapTrayToggle_UpdatesAndTracksSharedSetting()
        {
            var eventAggregator = new EventAggregator();
            var matchService = new Mock<IMatchService>();
            var resourceService = new Mock<IResourceService>();
            var automationSettings = new Mock<IGameAutomationSettings>();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string key) => key);
            automationSettings.SetupProperty(settings => settings.AutoSwapAramBench);
            automationSettings.SetupGet(settings => settings.LastPersistenceSucceeded)
                .Returns(true);
            var viewModel = new MainWindowViewModel(
                new Mock<IRegionManager>().Object,
                eventAggregator,
                new Mock<IModuleManager>().Object,
                matchService.Object,
                new Mock<IClientService>().Object,
                new Mock<IClientListener>().Object,
                resourceService.Object,
                new Mock<IProfilePresentationStartupService>().Object,
                automationSettings.Object,
                new Mock<IUpdateService>().Object,
                new Mock<IDialogService>().Object,
                new Mock<IGameService>().Object,
                CreateQuickMatchSettings().Object,
                CreateCompanionSettings().Object);

            viewModel.IsTrayAramSwapEnabled = true;

            Assert.True(automationSettings.Object.AutoSwapAramBench);

            var changedProperties = new List<string>();
            viewModel.PropertyChanged += (_, args) =>
                changedProperties.Add(args.PropertyName);
            automationSettings.Object.AutoSwapAramBench = false;
            automationSettings.Raise(
                settings => settings.Changed += null,
                EventArgs.Empty);

            Assert.False(viewModel.IsTrayAramSwapEnabled);
            Assert.Contains(nameof(MainWindowViewModel.IsTrayAramSwapEnabled),
                changedProperties);
        }

        [Fact]
        public void CompanionTrayToggle_UpdatesAndTracksSharedSetting()
        {
            var matchService = new Mock<IMatchService>();
            var companionSettings = CreateCompanionSettings();
            var resourceService = CreateQuickMatchResourceService();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);
            var viewModel = new MainWindowViewModel(
                new Mock<IRegionManager>().Object,
                new EventAggregator(),
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
                CreateQuickMatchSettings().Object,
                companionSettings.Object);

            viewModel.IsTrayCompanionEnabled = false;

            Assert.False(companionSettings.Object.IsEnabled);

            var changedProperties = new List<string>();
            viewModel.PropertyChanged += (_, args) =>
                changedProperties.Add(args.PropertyName);
            companionSettings.Object.IsEnabled = true;
            companionSettings.Raise(
                settings => settings.PropertyChanged += null,
                new PropertyChangedEventArgs(
                    nameof(ILcuCompanionSettings.IsEnabled)));

            Assert.True(viewModel.IsTrayCompanionEnabled);
            Assert.Contains(nameof(MainWindowViewModel.IsTrayCompanionEnabled),
                changedProperties);
        }

        [Fact]
        public async Task TrayQuickMatchLast_UsesPersistedQueueWhenConnectedAndIdle()
        {
            var invoked = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var matchService = CreateMatchService();
            var gameService = new Mock<IGameService>();
            var quickMatchSettings = CreateQuickMatchSettings(GameQueueIds.Aram);
            gameService.Setup(service => service.CreateMatchmadeLobbyAsync(
                    GameQueueIds.Aram,
                    It.IsAny<CancellationToken>()))
                .Callback<int, CancellationToken>((queueId, _) =>
                    invoked.TrySetResult(queueId))
                .ReturnsAsync(new MatchmadeLobbyCreationResult
                {
                    Status = MatchmadeLobbyCreationStatus.Created,
                    QueueId = GameQueueIds.Aram
                });
            var viewModel = CreateViewModel(
                matchService,
                gameService,
                quickMatchSettings,
                CreateQuickMatchResourceService());

            Assert.False(viewModel.IsTrayQuickMatchAvailable);

            PublishIdleSnapshot(matchService);

            Assert.True(viewModel.IsTrayQuickMatchAvailable);
            Assert.True(viewModel.QuickStartLastFromTrayCommand.CanExecute());

            viewModel.QuickStartLastFromTrayCommand.Execute();

            Assert.Equal(GameQueueIds.Aram,
                await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            quickMatchSettings.Verify(settings =>
                settings.SaveQueueId(It.IsAny<int>()), Times.Never);

            matchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(new LiveMatchSnapshot
                {
                    ConnectionState = ConnectionState.Connected,
                    GameflowPhase = GameflowPhase.Lobby,
                    UpdatedAt = DateTimeOffset.UtcNow
                }));

            Assert.True(viewModel.IsTrayQuickMatchAvailable);

            matchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(new LiveMatchSnapshot
                {
                    ConnectionState = ConnectionState.Connected,
                    GameflowPhase = GameflowPhase.Matchmaking,
                    UpdatedAt = DateTimeOffset.UtcNow
                }));

            Assert.False(viewModel.IsTrayQuickMatchAvailable);
        }

        [Theory]
        [InlineData(GameQueueIds.RankedSoloDuo)]
        [InlineData(GameQueueIds.RankedFlex)]
        [InlineData(GameQueueIds.Aram)]
        [InlineData(GameQueueIds.HextechAram)]
        public async Task TrayQuickMatchSelection_WhenAlreadyInLobby_ChangesQueue(
            int queueId)
        {
            var invoked = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var matchService = CreateMatchService();
            var gameService = new Mock<IGameService>();
            var quickMatchSettings = CreateQuickMatchSettings();
            gameService.Setup(service => service.CreateMatchmadeLobbyAsync(
                    queueId,
                    It.IsAny<CancellationToken>()))
                .Callback<int, CancellationToken>((value, _) =>
                    invoked.TrySetResult(value))
                .ReturnsAsync(new MatchmadeLobbyCreationResult
                {
                    Status = MatchmadeLobbyCreationStatus.Created,
                    QueueId = queueId
                });
            var viewModel = CreateViewModel(
                matchService,
                gameService,
                quickMatchSettings,
                CreateQuickMatchResourceService());
            matchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(new LiveMatchSnapshot
                {
                    ConnectionState = ConnectionState.Connected,
                    GameflowPhase = GameflowPhase.Lobby,
                    UpdatedAt = DateTimeOffset.UtcNow
                }));

            var command = queueId switch
            {
                GameQueueIds.RankedSoloDuo => viewModel.QuickStartSoloDuoFromTrayCommand,
                GameQueueIds.RankedFlex => viewModel.QuickStartFlexFromTrayCommand,
                GameQueueIds.Aram => viewModel.QuickStartAramFromTrayCommand,
                GameQueueIds.HextechAram => viewModel.QuickStartHextechAramFromTrayCommand,
                _ => throw new ArgumentOutOfRangeException(nameof(queueId))
            };
            command.Execute();

            Assert.Equal(queueId,
                await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            quickMatchSettings.Verify(settings =>
                settings.SaveQueueId(queueId), Times.Once);
        }

        [Fact]
        public void QuickMatchSettingsChanged_RefreshesTrayLastModeText()
        {
            var queueId = GameQueueIds.RankedSoloDuo;
            var quickMatchSettings = new Mock<IQuickMatchSettings>();
            quickMatchSettings.SetupGet(settings => settings.QueueId)
                .Returns(() => queueId);
            quickMatchSettings.Setup(settings => settings.SaveQueueId(
                    It.IsAny<int>()))
                .Returns(true);
            var viewModel = CreateViewModel(
                CreateMatchService(),
                new Mock<IGameService>(),
                quickMatchSettings,
                CreateQuickMatchResourceService());

            Assert.Equal("Quick start · Solo/Duo",
                viewModel.TrayQuickMatchLastText);

            queueId = GameQueueIds.HextechAram;
            quickMatchSettings.Raise(settings => settings.Changed += null,
                EventArgs.Empty);

            Assert.Equal("Quick start · ARAM Mayhem",
                viewModel.TrayQuickMatchLastText);
        }

        private static MainWindowViewModel CreateViewModel(
            Mock<IMatchService> matchService,
            Mock<IGameService> gameService,
            Mock<IQuickMatchSettings> quickMatchSettings,
            Mock<IResourceService> resourceService)
        {
            return new MainWindowViewModel(
                new Mock<IRegionManager>().Object,
                new EventAggregator(),
                new Mock<IModuleManager>().Object,
                matchService.Object,
                new Mock<IClientService>().Object,
                new Mock<IClientListener>().Object,
                resourceService.Object,
                new Mock<IProfilePresentationStartupService>().Object,
                new Mock<IGameAutomationSettings>().Object,
                new Mock<IUpdateService>().Object,
                new Mock<IDialogService>().Object,
                gameService.Object,
                quickMatchSettings.Object,
                CreateCompanionSettings().Object);
        }

        private static Mock<IMatchService> CreateMatchService()
        {
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);
            return matchService;
        }

        private static void PublishIdleSnapshot(Mock<IMatchService> matchService)
        {
            matchService.Raise(service => service.SnapshotChanged += null,
                new LiveMatchSnapshotChangedEventArgs(new LiveMatchSnapshot
                {
                    ConnectionState = ConnectionState.Connected,
                    GameflowPhase = GameflowPhase.None,
                    UpdatedAt = DateTimeOffset.UtcNow
                }));
        }

        private static Mock<IResourceService> CreateQuickMatchResourceService()
        {
            var resourceService = new Mock<IResourceService>();
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string key) => key switch
                {
                    "Tray.QuickMatch" => "Quick start",
                    "HomePage.QuickMatch.Button" => "Quick start · {0}",
                    "HomePage.QuickMatch.SoloDuo" => "Solo/Duo",
                    "HomePage.QuickMatch.Flex" => "Ranked Flex",
                    "HomePage.QuickMatch.Aram" => "ARAM",
                    "HomePage.QuickMatch.HextechAram" => "ARAM Mayhem",
                    "HomePage.QuickMatch.Created" => "Entered the {0} lobby",
                    _ => key
                });
            return resourceService;
        }

        private static Mock<IQuickMatchSettings> CreateQuickMatchSettings(
            int queueId = GameQueueIds.RankedSoloDuo)
        {
            var settings = new Mock<IQuickMatchSettings>();
            settings.SetupGet(value => value.QueueId).Returns(queueId);
            settings.Setup(value => value.SaveQueueId(It.IsAny<int>()))
                .Returns(true);
            return settings;
        }

        private static Mock<ILcuCompanionSettings> CreateCompanionSettings()
        {
            var settings = new Mock<ILcuCompanionSettings>();
            settings.SetupProperty(value => value.IsEnabled, true);
            settings.SetupGet(value => value.LastPersistenceSucceeded).Returns(true);
            return settings;
        }
    }
}
