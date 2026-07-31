using Prometheus.Update;

namespace Prometheus.Update.Tests;

public sealed class UpdateValidationTests
{
    [Fact]
    public void ValidateManifest_WhenSchemaIsUnsupported_Throws()
    {
        var manifest = ValidManifest();
        manifest.SchemaVersion = 2;

        Assert.Throws<InvalidDataException>(() =>
            UpdateValidation.ValidateManifest(manifest, "1.0.0"));
    }

    [Fact]
    public void ValidateManifest_WhenDesktopExecutableIsMissing_Throws()
    {
        var manifest = ValidManifest();
        manifest.Files[0].Path = "library.dll";

        Assert.Throws<InvalidDataException>(() =>
            UpdateValidation.ValidateManifest(manifest, "1.0.0"));
    }

    [Fact]
    public void ValidateManifest_WhenPathIsNotCanonical_Throws()
    {
        var manifest = ValidManifest();
        manifest.Files.Add(new ReleaseFileEntry
        {
            Path = "modules\\library.dll",
            Size = 1,
            Sha256 = new string('b', 64)
        });

        Assert.Throws<InvalidDataException>(() =>
            UpdateValidation.ValidateManifest(manifest, "1.0.0"));
    }

    private static ReleaseManifest ValidManifest()
    {
        return new ReleaseManifest
        {
            Version = "1.0.0",
            Files =
            [
                new ReleaseFileEntry
                {
                    Path = UpdateProtocol.DesktopExecutableName,
                    Size = 1,
                    Sha256 = new string('a', 64)
                }
            ]
        };
    }
}
