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
    public class LoggingPreferenceViewModelTests
    {
        [Fact]
        public void LoggingEnabled_UsesControlServiceAndUpdatesDisplayedCount()
        {
            var enabled = false;
            var loggingControl = new Mock<ILoggingControlService>();
            loggingControl.SetupGet(service => service.IsEnabled)
                .Returns(() => enabled);
            loggingControl.Setup(service => service.SetEnabled(It.IsAny<bool>()))
                .Callback((bool value) =>
                {
                    enabled = value;
                    loggingControl.Raise(
                        service => service.EnabledChanged += null,
                        EventArgs.Empty);
                });
            var logHistory = new Mock<ILogHistoryService>();
            logHistory.SetupGet(service => service.Capacity).Returns(100);
            logHistory.Setup(service => service.GetSnapshot())
                .Returns(() => enabled
                    ? new[] { CreateEntry() }
                    : Array.Empty<LogEntry>());
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current)
                .Returns(LiveMatchSnapshot.Empty);
            var resourceService = new Mock<IResourceService>();
            resourceService.Setup(service => service.FindResource<string>(It.IsAny<string>()))
                .Returns((string key) => key);
            var updateService = new Mock<IUpdateService>();
            var preferenceSettings = new Mock<IApplicationPreferenceSettings>();
            preferenceSettings.SetupGet(settings => settings.LanguageIndex).Returns(0);
            preferenceSettings.SetupGet(settings => settings.ThemeIndex).Returns(0);
            var viewModel = new PreferenceViewModel(
                new EventAggregator(),
                resourceService.Object,
                new Mock<IGameAutomationSettings>().Object,
                matchService.Object,
                logHistory.Object,
                loggingControl.Object,
                updateService.Object,
                preferenceSettings.Object);

            Assert.False(viewModel.LoggingEnabled);
            Assert.Equal(0, viewModel.LogCount);

            viewModel.LoggingEnabled = true;

            loggingControl.Verify(service => service.SetEnabled(true), Times.Once);
            Assert.True(viewModel.LoggingEnabled);
            Assert.Equal(1, viewModel.LogCount);
            viewModel.Destroy();
        }

        private static LogEntry CreateEntry()
        {
            return new LogEntry(
                DateTimeOffset.Now,
                LogLevel.Information,
                "Test",
                null);
        }
    }
}
