using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Prometheus.ReleaseTool;
using Prometheus.Update;

namespace Prometheus.Update.Tests;

public sealed class ReleaseBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "prometheus-release-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildAsync_WhenOnlyOneFileChanges_CreatesDirectDelta()
    {
        Directory.CreateDirectory(_root);
        var repository = Path.Combine(_root, "repo");
        var publish = Path.Combine(_root, "publish");
        var bootstrapper = Path.Combine(_root, "Prometheus.exe");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(publish);
        await File.WriteAllTextAsync(Path.Combine(repository, "Directory.Build.props"),
            "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");
        await File.WriteAllBytesAsync(Path.Combine(publish, "Prometheus.Desktop.exe"), [1]);
        var unchanged = new byte[128 * 1024];
        RandomNumberGenerator.Fill(unchanged);
        await File.WriteAllBytesAsync(Path.Combine(publish, "runtime.bin"), unchanged);
        await File.WriteAllBytesAsync(bootstrapper, [2]);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var first = await ReleaseBuilder.BuildAsync(CreateOptions("1.0.0", repository,
            publish, bootstrapper, Path.Combine(_root, "out-1")), key, []);

        await File.WriteAllTextAsync(Path.Combine(repository, "Directory.Build.props"),
            "<Project><PropertyGroup><Version>1.1.0</Version></PropertyGroup></Project>");
        await File.WriteAllBytesAsync(Path.Combine(publish, "Prometheus.Desktop.exe"), [3, 4]);
        var second = await ReleaseBuilder.BuildAsync(CreateOptions("1.1.0", repository,
            publish, bootstrapper, Path.Combine(_root, "out-2")), key,
            [first.ManifestEnvelope]);

        var delta = Assert.Single(second.Descriptor.Deltas);
        Assert.Equal("1.0.0", delta.BaseVersion);
        using var archive = ZipFile.OpenRead(Path.Combine(_root, "out-2",
            "delta-from-1.0.0.zip"));
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("Prometheus.Desktop.exe", entry.FullName);
    }

    [Fact]
    public async Task BuildAsync_WithSameInputs_CreatesDeterministicManifestAndPackagePayloads()
    {
        Directory.CreateDirectory(_root);
        var repository = Path.Combine(_root, "repo-deterministic");
        var publish = Path.Combine(_root, "publish-deterministic");
        var bootstrapper = Path.Combine(_root, "bootstrapper-deterministic.exe");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(publish);
        await File.WriteAllTextAsync(Path.Combine(repository, "Directory.Build.props"),
            "<Project><PropertyGroup><Version>1.1.0</Version></PropertyGroup></Project>");
        await File.WriteAllBytesAsync(Path.Combine(publish, "Prometheus.Desktop.exe"), [9]);
        var runtime = new byte[128 * 1024];
        RandomNumberGenerator.Fill(runtime);
        await File.WriteAllBytesAsync(Path.Combine(publish, "runtime.bin"), runtime);
        await File.WriteAllBytesAsync(bootstrapper, [2]);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var previous = UpdateSecurity.Sign(new ReleaseManifest
        {
            Version = "1.0.0",
            Files =
            [
                new ReleaseFileEntry
                {
                    Path = "Prometheus.Desktop.exe",
                    Size = 1,
                    Sha256 = new string('1', 64)
                },
                new ReleaseFileEntry
                {
                    Path = "runtime.bin",
                    Size = runtime.Length,
                    Sha256 = await UpdateSecurity.ComputeSha256Async(
                        Path.Combine(publish, "runtime.bin"))
                }
            ]
        }, key, UpdateJsonContext.Default.ReleaseManifest);

        var first = await ReleaseBuilder.BuildAsync(CreateOptions("1.1.0", repository,
            publish, bootstrapper, Path.Combine(_root, "deterministic-1")), key, [previous]);
        var second = await ReleaseBuilder.BuildAsync(CreateOptions("1.1.0", repository,
            publish, bootstrapper, Path.Combine(_root, "deterministic-2")), key, [previous]);

        Assert.Equal(first.ManifestEnvelope.Payload, second.ManifestEnvelope.Payload);
        Assert.Equal(await HashAsync(Path.Combine(_root, "deterministic-1", "full.zip")),
            await HashAsync(Path.Combine(_root, "deterministic-2", "full.zip")));
        Assert.Equal(await HashAsync(Path.Combine(_root, "deterministic-1",
                "delta-from-1.0.0.zip")),
            await HashAsync(Path.Combine(_root, "deterministic-2",
                "delta-from-1.0.0.zip")));
    }

    [Fact]
    public async Task BuildAsync_UsesOnlyThreeMostRecentDirectDeltas()
    {
        Directory.CreateDirectory(_root);
        var repository = Path.Combine(_root, "repo-recent");
        var publish = Path.Combine(_root, "publish-recent");
        var bootstrapper = Path.Combine(_root, "bootstrapper-recent.exe");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(publish);
        await File.WriteAllTextAsync(Path.Combine(repository, "Directory.Build.props"),
            "<Project><PropertyGroup><Version>2.0.0</Version></PropertyGroup></Project>");
        await File.WriteAllBytesAsync(Path.Combine(publish, "Prometheus.Desktop.exe"), [9]);
        var runtime = new byte[128 * 1024];
        RandomNumberGenerator.Fill(runtime);
        var runtimePath = Path.Combine(publish, "runtime.bin");
        await File.WriteAllBytesAsync(runtimePath, runtime);
        await File.WriteAllBytesAsync(bootstrapper, [2]);
        var runtimeHash = await UpdateSecurity.ComputeSha256Async(runtimePath);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var previous = new[] { "1.3.0", "1.2.0", "1.1.0", "1.0.0" }
            .Select(version => UpdateSecurity.Sign(new ReleaseManifest
            {
                Version = version,
                Files =
                [
                    new ReleaseFileEntry
                    {
                        Path = "Prometheus.Desktop.exe",
                        Size = 1,
                        Sha256 = new string(version[2], 64)
                    },
                    new ReleaseFileEntry
                    {
                        Path = "runtime.bin",
                        Size = runtime.Length,
                        Sha256 = runtimeHash
                    }
                ]
            }, key, UpdateJsonContext.Default.ReleaseManifest))
            .ToArray();

        var result = await ReleaseBuilder.BuildAsync(CreateOptions("2.0.0", repository,
            publish, bootstrapper, Path.Combine(_root, "recent-out")), key, previous);

        Assert.Equal(new[] { "1.3.0", "1.2.0", "1.1.0" },
            result.Descriptor.Deltas.Select(delta => delta.BaseVersion));
    }

    [Fact]
    public async Task BuildAsync_WhenDeltaReachesSeventyPercent_DoesNotPublishDelta()
    {
        Directory.CreateDirectory(_root);
        var repository = Path.Combine(_root, "repo-threshold");
        var publish = Path.Combine(_root, "publish-threshold");
        var bootstrapper = Path.Combine(_root, "bootstrapper-threshold.exe");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(publish);
        await File.WriteAllTextAsync(Path.Combine(repository, "Directory.Build.props"),
            "<Project><PropertyGroup><Version>1.1.0</Version></PropertyGroup></Project>");
        var desktop = new byte[128 * 1024];
        RandomNumberGenerator.Fill(desktop);
        await File.WriteAllBytesAsync(Path.Combine(publish, "Prometheus.Desktop.exe"), desktop);
        await File.WriteAllBytesAsync(bootstrapper, [2]);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var previous = UpdateSecurity.Sign(new ReleaseManifest
        {
            Version = "1.0.0",
            Files =
            [
                new ReleaseFileEntry
                {
                    Path = "Prometheus.Desktop.exe",
                    Size = 1,
                    Sha256 = new string('1', 64)
                }
            ]
        }, key, UpdateJsonContext.Default.ReleaseManifest);

        var result = await ReleaseBuilder.BuildAsync(CreateOptions("1.1.0", repository,
            publish, bootstrapper, Path.Combine(_root, "threshold-out")), key, [previous]);

        Assert.Empty(result.Descriptor.Deltas);
        Assert.False(File.Exists(Path.Combine(_root, "threshold-out",
            "delta-from-1.0.0.zip")));
    }

    private static Task<string> HashAsync(string path)
    {
        return UpdateSecurity.ComputeSha256Async(path);
    }

    private static ReleaseOptions CreateOptions(string version, string repository,
        string publish, string bootstrapper, string output)
    {
        return ReleaseOptions.Parse([
            "--version", version,
            "--git-tag", $"v{version}",
            "--publish-dir", publish,
            "--bootstrapper", bootstrapper,
            "--output", output,
            "--private-key", bootstrapper,
            "--repository-root", repository,
            "--account-id", "test",
            "--access-key", "test",
            "--secret-key", "test",
            "--bucket", "test",
            "--bootstrapper-version", version
        ]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
