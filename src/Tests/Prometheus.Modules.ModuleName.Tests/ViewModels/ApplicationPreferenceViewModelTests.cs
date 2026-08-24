using Moq;
using Prism.Events;
using Prometheus.Core.Models;
using Prometheus.Modules.Setting.ViewModels;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class ApplicationPreferenceViewModelTests
    {
        [Fact]
        public void LanguageAndThemeChanges_AreAppliedAndPersisted()
        {
            var preferenceSettings = new Mock<IApplicationPreferenceSettings>();
            preferenceSettings.SetupGet(settings => settings.LanguageIndex).Returns(0);
            preferenceSettings.SetupGet(settings => settings.ThemeIndex).Returns(0);
            preferenceSettings.Setup(settings => settings.SaveLanguageIndex(It.IsAny<int>()))
                .Returns(true);
            preferenceSettings.Setup(settings => settings.SaveThemeIndex(It.IsAny<int>()))
                .Returns(true);
            var resourceService = new Mock<IResourceService>();
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string key) => key);
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);
            var logHistory = new Mock<ILogHistoryService>();
            logHistory.SetupGet(service => service.Capacity).Returns(100);
            logHistory.Setup(service => service.GetSnapshot())
                .Returns(Array.Empty<LogEntry>());
            var viewModel = new PreferenceViewModel(
                new EventAggregator(),
                resourceService.Object,
                new Mock<IGameAutomationSettings>().Object,
                matchService.Object,
                logHistory.Object,
                new Mock<ILoggingControlService>().Object,
                new Mock<IUpdateService>().Object,
                preferenceSettings.Object);

            try
            {
                viewModel.SelectedLanguageIndex = 1;
                viewModel.SelectedThemeIndex = (int)ApplicationThemeMode.System;

                resourceService.Verify(service => service.SwitchLanguage(1), Times.Once);
                resourceService.Verify(
                    service => service.SwitchTheme((int)ApplicationThemeMode.System),
                    Times.Once);
                preferenceSettings.Verify(
                    settings => settings.SaveLanguageIndex(1), Times.Once);
                preferenceSettings.Verify(
                    settings => settings.SaveThemeIndex((int)ApplicationThemeMode.System),
                    Times.Once);
            }
            finally
            {
                viewModel.Destroy();
            }
        }
    }
}
