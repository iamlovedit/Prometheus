using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Prometheus.Update;
using Prometheus.Updater;

namespace Prometheus.Update.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "prometheus-installer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildTargetVersionAsync_WithFullPackage_CreatesVerifiedVersionDirectory()
    {
        Directory.CreateDirectory(_root);
        var package = Path.Combine(_root, "full.zip");
        await File.WriteAllBytesAsync(Path.Combine(_root, "app.bin"), [1, 2, 3]);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Path.Combine(_root, "app.bin"),
                "Prometheus.Desktop.exe");
        }
        var hash = await UpdateSecurity.ComputeSha256Async(Path.Combine(_root, "app.bin"));
        var manifestPath = Path.Combine(_root, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{}");
        var plan = new UpdateApplyPlan
        {
            InstallRoot = _root,
            CurrentVersion = "1.0.0",
            TargetVersion = "1.1.0",
            PackageKind = UpdatePackageKind.Full,
            PackagePath = package,
            TargetManifestPath = manifestPath
        };
        var manifest = new ReleaseManifest
        {
            Version = "1.1.0",
            Files = [new ReleaseFileEntry
            {
                Path = "Prometheus.Desktop.exe",
                Size = 3,
                Sha256 = hash
            }]
        };

        var target = await UpdateInstaller.BuildTargetVersionAsync(plan, manifest);

        Assert.True(File.Exists(Path.Combine(target, "Prometheus.Desktop.exe")));
        Assert.True(File.Exists(Path.Combine(target,
            UpdateProtocol.InstalledManifestFileName)));
    }

    [Fact]
    public async Task BuildTargetVersionAsync_WithTraversalEntry_RejectsPackage()
    {
        Directory.CreateDirectory(_root);
        var package = Path.Combine(_root, "full.zip");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            await using (var desktop = archive.CreateEntry(
                             UpdateProtocol.DesktopExecutableName).Open())
            {
                await desktop.WriteAsync(new byte[] { 1 });
            }
            await using (var escape = archive.CreateEntry("../escape.dll").Open())
            {
                await escape.WriteAsync(new byte[] { 2 });
            }
        }
        var manifestPath = Path.Combine(_root, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{}");
        var plan = new UpdateApplyPlan
        {
            InstallRoot = _root,
            CurrentVersion = "1.0.0",
            TargetVersion = "1.1.0",
            PackageKind = UpdatePackageKind.Full,
            PackagePath = package,
            TargetManifestPath = manifestPath
        };
        var manifest = new ReleaseManifest
        {
            Version = "1.1.0",
            Files =
            [
                new ReleaseFileEntry
                {
                    Path = UpdateProtocol.DesktopExecutableName,
                    Size = 1,
                    Sha256 = await HashAsync([1])
                }
            ]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateInstaller.BuildTargetVersionAsync(plan, manifest));
        Assert.False(File.Exists(Path.Combine(_root, "escape.dll")));
    }

    [Fact]
    public async Task BuildTargetVersionAsync_WithDelta_ReusesUnchangedAndOmitsDeletedFiles()
    {
        Directory.CreateDirectory(_root);
        var baseRoot = Path.Combine(_root, "versions", "1.0.0");
        Directory.CreateDirectory(baseRoot);
        var oldDesktop = new byte[] { 1 };
        var newDesktop = new byte[] { 2, 3 };
        var unchanged = new byte[64 * 1024];
        RandomNumberGenerator.Fill(unchanged);
        await File.WriteAllBytesAsync(Path.Combine(baseRoot,
            UpdateProtocol.DesktopExecutableName), oldDesktop);
        await File.WriteAllBytesAsync(Path.Combine(baseRoot, "runtime.bin"), unchanged);
        await File.WriteAllBytesAsync(Path.Combine(baseRoot, "removed.bin"), [9]);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var baseManifest = new ReleaseManifest
        {
            Version = "1.0.0",
            Files =
            [
                Entry(UpdateProtocol.DesktopExecutableName, oldDesktop),
                Entry("runtime.bin", unchanged),
                Entry("removed.bin", [9])
            ]
        };
        var baseEnvelope = UpdateSecurity.Sign(baseManifest, key,
            UpdateJsonContext.Default.ReleaseManifest);
        await File.WriteAllBytesAsync(Path.Combine(baseRoot,
                UpdateProtocol.InstalledManifestFileName),
            JsonSerializer.SerializeToUtf8Bytes(baseEnvelope,
                UpdateJsonContext.Default.SignedEnvelope));
        var package = Path.Combine(_root, "delta.zip");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(UpdateProtocol.DesktopExecutableName);
            await using var stream = entry.Open();
            await stream.WriteAsync(newDesktop);
        }
        var targetManifestPath = Path.Combine(_root, "target-manifest.json");
        await File.WriteAllTextAsync(targetManifestPath, "{}");
        var plan = new UpdateApplyPlan
        {
            InstallRoot = _root,
            CurrentVersion = "1.0.0",
            TargetVersion = "1.1.0",
            PackageKind = UpdatePackageKind.Delta,
            PackagePath = package,
            TargetManifestPath = targetManifestPath
        };
        var targetManifest = new ReleaseManifest
        {
            Version = "1.1.0",
            Files =
            [
                Entry(UpdateProtocol.DesktopExecutableName, newDesktop),
                Entry("runtime.bin", unchanged)
            ]
        };
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);

        var target = await UpdateInstaller.BuildTargetVersionAsync(plan, targetManifest,
            envelope => UpdateSecurity.VerifyAndDeserialize(envelope, publicKey,
                UpdateJsonContext.Default.ReleaseManifest));

        Assert.Equal(newDesktop, await File.ReadAllBytesAsync(Path.Combine(target,
            UpdateProtocol.DesktopExecutableName)));
        Assert.Equal(unchanged, await File.ReadAllBytesAsync(Path.Combine(target,
            "runtime.bin")));
        Assert.False(File.Exists(Path.Combine(target, "removed.bin")));
    }

    private static ReleaseFileEntry Entry(string path, byte[] bytes)
    {
        return new ReleaseFileEntry
        {
            Path = path,
            Size = bytes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes))
        };
    }

    private async Task<string> HashAsync(byte[] bytes)
    {
        var path = Path.Combine(_root, $"hash-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(path, bytes);
        return await UpdateSecurity.ComputeSha256Async(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
