using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Prometheus.Update;
using Prometheus.Updater;

namespace Prometheus.Update.Tests;

public sealed class BootstrapperIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "prometheus-bootstrapper-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyAsync_WhenHealthSucceeds_CommitsTargetAndKeepsRollback()
    {
        var fixture = await CreateFixtureAsync();
        var launchedVersions = new List<string>();

        var result = await Bootstrapper.ApplyAsync(fixture.PlanPath,
            CreateHooks(launchedVersions, true));

        Assert.Equal(0, result);
        Assert.Equal(fixture.NewDesktop, await File.ReadAllBytesAsync(Path.Combine(
            fixture.InstallRoot, UpdateProtocol.DesktopExecutableName)));
        Assert.Equal(fixture.OldDesktop, await File.ReadAllBytesAsync(Path.Combine(
            fixture.InstallRoot + ".rollback", UpdateProtocol.DesktopExecutableName)));
        Assert.Equal(new[] { "2.0.0" }, launchedVersions);
        Assert.False(File.Exists(fixture.PackagePath));
        Assert.False(File.Exists(fixture.PlanPath));
    }

    [Fact]
    public async Task ApplyAsync_WhenHealthFails_RestoresAndLaunchesPreviousVersion()
    {
        var fixture = await CreateFixtureAsync();
        var launchedVersions = new List<string>();

        var result = await Bootstrapper.ApplyAsync(fixture.PlanPath,
            CreateHooks(launchedVersions, false));

        Assert.Equal(2, result);
        Assert.Equal(fixture.OldDesktop, await File.ReadAllBytesAsync(Path.Combine(
            fixture.InstallRoot, UpdateProtocol.DesktopExecutableName)));
        Assert.Equal(new[] { "2.0.0", "1.0.0" }, launchedVersions);
        Assert.False(Directory.Exists(fixture.InstallRoot + ".rollback"));
    }

    [Fact]
    public async Task ApplyAsync_WhenPackageValidationFails_RestartsCurrentVersion()
    {
        var fixture = await CreateFixtureAsync();
        var launchedVersions = new List<string>();
        var hooks = CreateHooks(launchedVersions, true);
        hooks = new BootstrapperTestHooks
        {
            ValidateDesktopVersion = (_, _) => false,
            StartDesktop = hooks.StartDesktop,
            WaitForHealth = hooks.WaitForHealth
        };

        var result = await Bootstrapper.ApplyAsync(fixture.PlanPath, hooks);

        Assert.Equal(1, result);
        Assert.Equal(new[] { "1.0.0" }, launchedVersions);
        Assert.Equal(fixture.OldDesktop, await File.ReadAllBytesAsync(Path.Combine(
            fixture.InstallRoot, UpdateProtocol.DesktopExecutableName)));
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        var installRoot = Path.Combine(_root, "Prometheus");
        Directory.CreateDirectory(installRoot);
        var oldDesktop = new byte[] { 1, 2, 3 };
        var newDesktop = new byte[] { 4, 5, 6, 7 };
        await File.WriteAllBytesAsync(Path.Combine(installRoot,
            UpdateProtocol.DesktopExecutableName), oldDesktop);
        await File.WriteAllBytesAsync(Path.Combine(installRoot, "old-only.dll"), [8]);

        Directory.CreateDirectory(_root);
        var packagePath = Path.Combine(_root, "update.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, UpdateProtocol.DesktopExecutableName, newDesktop);
            WriteEntry(archive, "new-only.dll", [9]);
        }
        var packageBytes = await File.ReadAllBytesAsync(packagePath);
        var plan = new UpdateApplyPlan
        {
            InstallRoot = installRoot,
            CurrentVersion = "1.0.0",
            TargetVersion = "2.0.0",
            ParentProcessId = 0,
            HealthToken = Guid.NewGuid().ToString("D"),
            PackagePath = packagePath,
            PackageSize = packageBytes.Length,
            PackageSha256 = Convert.ToHexStringLower(SHA256.HashData(packageBytes))
        };
        var planPath = Path.Combine(_root, "plan.json");
        UpdatePaths.WriteJsonAtomic(planPath, plan, UpdateJsonContext.Default.UpdateApplyPlan);
        return new Fixture(installRoot, planPath, packagePath, oldDesktop, newDesktop);
    }

    private static BootstrapperTestHooks CreateHooks(List<string> launchedVersions,
        bool healthResult)
    {
        Process StartDesktop(string _, string version, string? __)
        {
            launchedVersions.Add(version);
            var command = Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is not available.");
            return Process.Start(new ProcessStartInfo(command, "/c ping 127.0.0.1 -n 3 > nul")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Unable to start test process.");
        }
        return new BootstrapperTestHooks
        {
            ValidateDesktopVersion = (_, _) => true,
            StartDesktop = StartDesktop,
            WaitForHealth = (_, _) => Task.FromResult(healthResult)
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

    private sealed record Fixture(string InstallRoot, string PlanPath, string PackagePath,
        byte[] OldDesktop, byte[] NewDesktop);
}
