using Moq;
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core.Models;
using Prometheus.Modules.Setting;
using Prometheus.Services.Interfaces.Client;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class ApplicationPreferenceInitializationTests
    {
        [Fact]
        public void ExistingStablePreferences_AreAppliedWithoutLegacyOverwrite()
        {
            var resourceService = new Mock<IResourceService>();
            var preferenceSettings = new Mock<IApplicationPreferenceSettings>();
            preferenceSettings.SetupGet(settings => settings.LanguageIndex).Returns(1);
            preferenceSettings.SetupGet(settings => settings.ThemeIndex)
                .Returns((int)ApplicationThemeMode.System);
            var module = new SettingModule(
                new Mock<IRegionManager>().Object,
                resourceService.Object,
                preferenceSettings.Object);

            module.OnInitialized(new Mock<IContainerProvider>().Object);

            resourceService.Verify(service => service.SwitchLanguage(1), Times.Once);
            resourceService.Verify(
                service => service.SwitchTheme((int)ApplicationThemeMode.System),
                Times.Once);
            preferenceSettings.Verify(
                settings => settings.SaveLanguageIndex(It.IsAny<int>()), Times.Never);
            preferenceSettings.Verify(
                settings => settings.SaveThemeIndex(It.IsAny<int>()), Times.Never);
        }
    }
}
