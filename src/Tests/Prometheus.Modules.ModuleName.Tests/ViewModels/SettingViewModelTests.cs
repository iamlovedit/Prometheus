using Moq;
using Prism.Regions;
using Prometheus.Modules.Setting.ViewModels;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels;

public sealed class SettingViewModelTests
{
    [Fact]
    public void OpenGitHubCommand_UsesRepositoryFromBuildOptions()
    {
        var externalLinkService = new Mock<IExternalLinkService>();
        externalLinkService.Setup(service => service.Open(It.IsAny<Uri>()))
            .Returns(true);
        var viewModel = CreateViewModel(externalLinkService.Object,
            new UpdateServiceOptions
            {
                GitHubOwner = "iamlovedit",
                GitHubRepository = "Prometheus"
            });

        viewModel.OpenGitHubCommand.Execute();

        externalLinkService.Verify(service => service.Open(It.Is<Uri>(uri =>
            uri.AbsoluteUri == "https://github.com/iamlovedit/Prometheus")),
            Times.Once);
    }

    [Fact]
    public void OpenGitHubCommand_WithoutRepositoryConfiguration_IsDisabled()
    {
        var externalLinkService = new Mock<IExternalLinkService>();
        var viewModel = CreateViewModel(externalLinkService.Object,
            new UpdateServiceOptions());

        Assert.False(viewModel.OpenGitHubCommand.CanExecute());
    }

    private static SettingViewModel CreateViewModel(
        IExternalLinkService externalLinkService,
        UpdateServiceOptions updateOptions)
    {
        return new SettingViewModel(
            new Mock<IRegionManager>().Object,
            externalLinkService,
            new Mock<IResourceService>().Object,
            updateOptions);
    }
}
