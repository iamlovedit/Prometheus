using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Prometheus.Update;
using Prometheus.Updater;

namespace Prometheus.Update.Tests;

public sealed class BootstrapperIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "prometheus-bootstrapper-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyAsync_WhenHealthSucceeds_CommitsTargetVersion()
    {
        var fixture = await CreateFixtureAsync();
        var launchedVersions = new List<string>();

        var result = await Bootstrapper.ApplyAsync(fixture.PlanPath,
            CreateHooks(fixture, launchedVersions, true));

        var state = BootstrapperStateStore.Load(_root);
        Assert.Equal(0, result);
        Assert.Equal("1.1.0", state.CurrentVersion);
        Assert.Equal("1.0.0", state.RollbackVersion);
        Assert.Null(state.PendingHealthToken);
        Assert.Equal(new[] { "1.1.0" }, launchedVersions);
        Assert.True(File.Exists(Path.Combine(_root, "versions", "1.1.0",
            UpdateProtocol.DesktopExecutableName)));
    }

    [Fact]
    public async Task ApplyAsync_WhenHealthFails_RestoresAndLaunchesPreviousVersion()
    {
        var fixture = await CreateFixtureAsync();
        var launchedVersions = new List<string>();

        var result = await Bootstrapper.ApplyAsync(fixture.PlanPath,
            CreateHooks(fixture, launchedVersions, false));

        var state = BootstrapperStateStore.Load(_root);
        Assert.Equal(2, result);
        Assert.Equal("1.0.0", state.CurrentVersion);
        Assert.Equal("1.1.0", state.RollbackVersion);
        Assert.Null(state.PendingHealthToken);
        Assert.Equal(new[] { "1.1.0", "1.0.0" }, launchedVersions);
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "versions", "1.0.0"));
        BootstrapperStateStore.Save(_root, new BootstrapperState
        {
            CurrentVersion = "1.0.0",
            BootstrapperVersion = "1.0.0"
        });
        var desktopBytes = new byte[] { 1, 2, 3 };
        var packagePath = Path.Combine(_root, "full.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(UpdateProtocol.DesktopExecutableName);
            await using var stream = entry.Open();
            await stream.WriteAsync(desktopBytes);
        }
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = new ReleaseManifest
        {
            Version = "1.1.0",
            Files =
            [
                new ReleaseFileEntry
                {
                    Path = UpdateProtocol.DesktopExecutableName,
                    Size = desktopBytes.Length,
                    Sha256 = Hash(desktopBytes)
                }
            ]
        };
        var manifestEnvelope = UpdateSecurity.Sign(manifest, key,
            UpdateJsonContext.Default.ReleaseManifest);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifestEnvelope,
            UpdateJsonContext.Default.SignedEnvelope);
        var manifestPath = Path.Combine(_root, "manifest.json");
        await File.WriteAllBytesAsync(manifestPath, manifestBytes);
        var packageBytes = await File.ReadAllBytesAsync(packagePath);
        var descriptor = new ReleaseDescriptor
        {
            Version = "1.1.0",
            MinimumSupportedVersion = "1.0.0",
            MinimumBootstrapperVersion = "1.0.0",
            BootstrapperVersion = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow,
            TargetManifest = Artifact("manifest", "manifest.json", manifestBytes),
            FullPackage = Artifact("full", "full.zip", packageBytes),
            PortablePackage = new UpdateArtifact
            {
                Id = "portable",
                ObjectKey = "releases/1.1.0/win-x64/portable.zip",
                Size = 1,
                Sha256 = new string('a', 64)
            }
        };
        var releaseEnvelope = UpdateSecurity.Sign(descriptor, key,
            UpdateJsonContext.Default.ReleaseDescriptor);
        var plan = new UpdateApplyPlan
        {
            InstallRoot = _root,
            CurrentVersion = "1.0.0",
            TargetVersion = "1.1.0",
            ParentProcessId = 0,
            HealthToken = Guid.NewGuid().ToString("D"),
            PackageKind = UpdatePackageKind.Full,
            SelectedArtifactId = "full",
            PackagePath = packagePath,
            TargetManifestPath = manifestPath,
            Release = releaseEnvelope
        };
        var planPath = Path.Combine(_root, "plan.json");
        UpdatePaths.WriteJsonAtomic(planPath, plan, UpdateJsonContext.Default.UpdateApplyPlan);
        return new Fixture(planPath, key.ExportSubjectPublicKeyInfo());
    }

    private static BootstrapperTestHooks CreateHooks(Fixture fixture,
        List<string> launchedVersions, bool healthResult)
    {
        ReleaseDescriptor VerifyRelease(SignedEnvelope envelope)
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(fixture.PublicKey, out _);
            return UpdateSecurity.VerifyAndDeserialize(envelope, key,
                UpdateJsonContext.Default.ReleaseDescriptor);
        }
        ReleaseManifest VerifyManifest(SignedEnvelope envelope)
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(fixture.PublicKey, out _);
            return UpdateSecurity.VerifyAndDeserialize(envelope, key,
                UpdateJsonContext.Default.ReleaseManifest);
        }
        Process StartDesktop(string _, string version, string? __)
        {
            launchedVersions.Add(version);
            var command = Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is not available.");
            return Process.Start(new ProcessStartInfo(command, "/c exit 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Unable to start test process.");
        }
        return new BootstrapperTestHooks
        {
            VerifyRelease = VerifyRelease,
            VerifyManifest = VerifyManifest,
            StartDesktop = StartDesktop,
            WaitForHealth = (_, _) => Task.FromResult(healthResult)
        };
    }

    private static UpdateArtifact Artifact(string id, string name, byte[] bytes)
    {
        return new UpdateArtifact
        {
            Id = id,
            ObjectKey = $"releases/1.1.0/win-x64/{name}",
            Size = bytes.Length,
            Sha256 = Hash(bytes)
        };
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed record Fixture(string PlanPath, byte[] PublicKey);
}
