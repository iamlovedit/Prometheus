using System.IO.Compression;
using System.Security.Cryptography;
using Prometheus.Update;
using Prometheus.Updater;

namespace Prometheus.Update.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "prometheus-installer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PrepareStagingAsync_WithFullPackage_ExtractsSafeFiles()
    {
        var plan = await CreatePlanAsync(archive =>
        {
            WriteEntry(archive, UpdateProtocol.DesktopExecutableName, [1, 2, 3]);
            WriteEntry(archive, "modules/example.dll", [4, 5]);
        });

        var staging = await UpdateInstaller.PrepareStagingAsync(plan, (_, _) => true);

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(
            Path.Combine(staging, UpdateProtocol.DesktopExecutableName)));
        Assert.Equal(new byte[] { 4, 5 }, await File.ReadAllBytesAsync(
            Path.Combine(staging, "modules", "example.dll")));
    }

    [Fact]
    public async Task PrepareStagingAsync_WithTraversalEntry_RejectsPackage()
    {
        var plan = await CreatePlanAsync(archive =>
        {
            WriteEntry(archive, UpdateProtocol.DesktopExecutableName, [1]);
            WriteEntry(archive, "../escape.dll", [2]);
        });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateInstaller.PrepareStagingAsync(plan, (_, _) => true));
        Assert.False(File.Exists(Path.Combine(_root, "escape.dll")));
    }

    [Fact]
    public async Task PrepareStagingAsync_WithSymbolicLink_RejectsPackage()
    {
        var plan = await CreatePlanAsync(archive =>
        {
            WriteEntry(archive, UpdateProtocol.DesktopExecutableName, [1]);
            var link = archive.CreateEntry("link.dll");
            link.ExternalAttributes = 0xA000 << 16;
            using var stream = link.Open();
            stream.WriteByte(2);
        });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateInstaller.PrepareStagingAsync(plan, (_, _) => true));
    }

    [Fact]
    public async Task PrepareStagingAsync_WhenDesktopVersionDiffers_RejectsPackage()
    {
        var plan = await CreatePlanAsync(archive =>
            WriteEntry(archive, UpdateProtocol.DesktopExecutableName, [1]));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateInstaller.PrepareStagingAsync(plan, (_, _) => false));
    }

    private async Task<UpdateApplyPlan> CreatePlanAsync(Action<ZipArchive> buildArchive)
    {
        var installRoot = Path.Combine(_root, "Prometheus");
        Directory.CreateDirectory(installRoot);
        var packagePath = Path.Combine(_root, $"package-{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            buildArchive(archive);
        }
        var packageBytes = await File.ReadAllBytesAsync(packagePath);
        return new UpdateApplyPlan
        {
            InstallRoot = installRoot,
            CurrentVersion = "1.0.0",
            TargetVersion = "2.0.0",
            HealthToken = Guid.NewGuid().ToString("D"),
            PackagePath = packagePath,
            PackageSize = packageBytes.Length,
            PackageSha256 = Convert.ToHexStringLower(SHA256.HashData(packageBytes))
        };
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
