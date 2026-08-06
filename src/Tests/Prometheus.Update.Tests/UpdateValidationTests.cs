using Prometheus.Update;

namespace Prometheus.Update.Tests;

public sealed class UpdateValidationTests
{
    [Fact]
    public void ValidateGitHubRelease_WithStableAssets_ReturnsSelection()
    {
        var release = ValidRelease();

        var selection = UpdateValidation.ValidateGitHubRelease(release,
            "iamlovedit", "Prometheus");

        Assert.Equal("2.0.0", selection.Version);
        Assert.Equal("Prometheus-2.0.0-win-x64.zip", selection.Package.Name);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ValidateGitHubRelease_WhenNotStable_Throws(bool draft, bool prerelease)
    {
        var release = ValidRelease();
        release.Draft = draft;
        release.Prerelease = prerelease;

        Assert.Throws<InvalidDataException>(() =>
            UpdateValidation.ValidateGitHubRelease(release, "iamlovedit", "Prometheus"));
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("v2.0")]
    [InlineData("v02.0.0")]
    [InlineData("v2.0.0-beta")]
    public void ValidateGitHubRelease_WhenTagIsInvalid_Throws(string tag)
    {
        var release = ValidRelease();
        release.TagName = tag;

        Assert.Throws<InvalidDataException>(() =>
            UpdateValidation.ValidateGitHubRelease(release, "iamlovedit", "Prometheus"));
    }

    [Fact]
    public void ValidateGitHubRelease_WhenPackageIsDuplicated_Throws()
    {
        var release = ValidRelease();
        release.Assets.Add(release.Assets[0]);

        Assert.Throws<InvalidDataException>(() =>
            UpdateValidation.ValidateGitHubRelease(release, "iamlovedit", "Prometheus"));
    }

    [Fact]
    public void ValidateGitHubRelease_WhenAssetUrlIsNotGitHub_Throws()
    {
        var release = ValidRelease();
        release.Assets[0].BrowserDownloadUrl = new Uri("https://example.com/update.zip");

        Assert.Throws<InvalidDataException>(() =>
            UpdateValidation.ValidateGitHubRelease(release, "iamlovedit", "Prometheus"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseSha256File_WithOptionalFileName_ReturnsCanonicalHash(bool includeName)
    {
        var hash = new string('A', 64);
        var content = includeName
            ? $"{hash}  Prometheus-2.0.0-win-x64.zip\n"
            : hash;

        var result = UpdateValidation.ParseSha256File(content,
            "Prometheus-2.0.0-win-x64.zip");

        Assert.Equal(new string('a', 64), result);
    }

    [Fact]
    public void ParseSha256File_WhenFileNameDiffers_Throws()
    {
        var content = $"{new string('a', 64)}  another.zip";

        Assert.Throws<InvalidDataException>(() => UpdateValidation.ParseSha256File(content,
            "Prometheus-2.0.0-win-x64.zip"));
    }

    private static GitHubRelease ValidRelease()
    {
        const string packageName = "Prometheus-2.0.0-win-x64.zip";
        return new GitHubRelease
        {
            TagName = "v2.0.0",
            Body = "Release notes",
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = packageName,
                    Size = 100,
                    BrowserDownloadUrl = new Uri(
                        $"https://github.com/iamlovedit/Prometheus/releases/download/v2.0.0/{packageName}")
                },
                new GitHubReleaseAsset
                {
                    Name = packageName + ".sha256",
                    Size = 64,
                    BrowserDownloadUrl = new Uri(
                        $"https://github.com/iamlovedit/Prometheus/releases/download/v2.0.0/{packageName}.sha256")
                }
            ]
        };
    }
}
