#nullable enable

using System.Diagnostics;
using Prometheus.Services;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services;

public sealed class ExternalLinkServiceTests
{
    [Fact]
    public void Open_WithHttpsUri_UsesDefaultBrowser()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var service = new ExternalLinkService(
            startInfo => capturedStartInfo = startInfo);

        var opened = service.Open(new Uri("https://github.com/iamlovedit/Prometheus"));

        Assert.True(opened);
        Assert.NotNull(capturedStartInfo);
        Assert.True(capturedStartInfo.UseShellExecute);
        Assert.Equal("https://github.com/iamlovedit/Prometheus",
            capturedStartInfo.FileName);
    }

    [Fact]
    public void Open_WithNonHttpsUri_DoesNotStartProcess()
    {
        var processStarted = false;
        var service = new ExternalLinkService(_ => processStarted = true);

        var opened = service.Open(new Uri("http://github.com/iamlovedit/Prometheus"));

        Assert.False(opened);
        Assert.False(processStarted);
    }

    [Fact]
    public void Open_WhenBrowserCannotStart_ReturnsFalse()
    {
        var service = new ExternalLinkService(
            _ => throw new InvalidOperationException("No browser"));

        var opened = service.Open(new Uri("https://github.com/iamlovedit/Prometheus"));

        Assert.False(opened);
    }
}
