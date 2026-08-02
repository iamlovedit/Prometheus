using Prometheus.Services.Client;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Diagnostics;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LeagueClientLauncherTests
    {
        [Fact]
        public void ReadRiotClientExecutable_PrefersLiveInstallation()
        {
            const string json = """
                {
                  "rc_default": "C:/Riot/Default/RiotClientServices.exe",
                  "rc_live": "D:/Riot/Live/RiotClientServices.exe"
                }
                """;

            var path = LeagueClientLauncher.ReadRiotClientExecutable(json);

            Assert.Equal("D:/Riot/Live/RiotClientServices.exe", path);
        }

        [Fact]
        public void ReadRiotClientExecutable_WhenDefaultIsMissing_UsesPatchline()
        {
            const string json = """
                {
                  "patchlines": {
                    "KeystoneFoundationTencentLeagueLivePublicWin":
                      "C:/Games/League/Riot Client/RiotClientServices.exe"
                  }
                }
                """;

            var path = LeagueClientLauncher.ReadRiotClientExecutable(json);

            Assert.Equal(
                "C:/Games/League/Riot Client/RiotClientServices.exe", path);
        }

        [Fact]
        public async Task LaunchAsync_StartsRiotClientWithLeagueArguments()
        {
            const string riotClientPath =
                @"D:\Riot Games\Riot Client\RiotClientServices.exe";
            ProcessStartInfo captured = null;
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new LeagueClientLauncher(
                () => false,
                () => LeagueClientLauncher.CreateLaunchTarget(
                    riotClientPath,
                    path => string.Equals(
                        path, riotClientPath, StringComparison.OrdinalIgnoreCase)),
                startInfo =>
                {
                    captured = startInfo;
                    return new Process();
                },
                logger);

            var result = await launcher.LaunchAsync();

            Assert.Equal(LeagueClientLaunchStatus.Started, result);
            Assert.NotNull(captured);
            Assert.Equal(riotClientPath, captured.FileName);
            Assert.Equal(@"D:\Riot Games\Riot Client", captured.WorkingDirectory);
            Assert.False(captured.UseShellExecute);
            Assert.Equal(
            [
                "--launch-product=league_of_legends",
                "--launch-patchline=live"
            ], captured.ArgumentList);
        }

        [Fact]
        public void CreateLaunchTarget_WhenTencentClientExists_PrefersTcls()
        {
            const string riotClientPath =
                @"C:\Games\League\Riot Client\RiotClientServices.exe";
            const string tencentClientPath =
                @"C:\Games\League\TCLS\Client.exe";
            const string weGameLauncherPath =
                @"C:\Games\League\WeGameLauncher\launcher.exe";

            var target = LeagueClientLauncher.CreateLaunchTarget(
                riotClientPath,
                path => string.Equals(
                    path, tencentClientPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        path, weGameLauncherPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        path, riotClientPath, StringComparison.OrdinalIgnoreCase));
            var startInfo = LeagueClientLauncher.CreateStartInfo(target);

            Assert.NotNull(target);
            Assert.False(target.RequiresExternalLauncher);
            Assert.Equal(tencentClientPath, target.ExecutablePath);
            Assert.Equal(@"C:\Games\League", target.WorkingDirectory);
            Assert.Empty(target.Arguments);
            Assert.True(target.UseShellExecute);
            Assert.Equal(tencentClientPath, startInfo.FileName);
            Assert.Equal(@"C:\Games\League", startInfo.WorkingDirectory);
            Assert.True(startInfo.UseShellExecute);
            Assert.Empty(startInfo.ArgumentList);
        }

        [Fact]
        public async Task LaunchAsync_WhenTencentClientExists_StartsTcls()
        {
            const string installDirectory = @"C:\Games\League";
            const string tencentClientPath =
                @"C:\Games\League\TCLS\Client.exe";
            ProcessStartInfo captured = null;
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new LeagueClientLauncher(
                () => false,
                () => LeagueClientLauncher.CreateTencentLaunchTarget(
                    installDirectory,
                    path => string.Equals(
                        path, tencentClientPath,
                        StringComparison.OrdinalIgnoreCase)),
                startInfo =>
                {
                    captured = startInfo;
                    return new Process();
                },
                logger);

            var result = await launcher.LaunchAsync();

            Assert.Equal(LeagueClientLaunchStatus.Started, result);
            Assert.NotNull(captured);
            Assert.Equal(tencentClientPath, captured.FileName);
            Assert.Equal(installDirectory, captured.WorkingDirectory);
            Assert.True(captured.UseShellExecute);
            Assert.Empty(captured.ArgumentList);
        }

        [Fact]
        public void CreateLaunchTarget_WhenOnlyWeGameExists_RequiresExternalLauncher()
        {
            const string riotClientPath =
                @"C:\Games\League\Riot Client\RiotClientServices.exe";
            const string weGameLauncherPath =
                @"C:\Games\League\WeGameLauncher\launcher.exe";

            var target = LeagueClientLauncher.CreateLaunchTarget(
                riotClientPath,
                path => string.Equals(
                    path, weGameLauncherPath,
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(target);
            Assert.True(target.RequiresExternalLauncher);
            Assert.Null(target.ExecutablePath);
            Assert.Empty(target.Arguments);
            Assert.False(target.UseShellExecute);
        }

        [Fact]
        public async Task LaunchAsync_WhenOnlyExternalLauncherIsAvailable_DoesNotStartAProcess()
        {
            var startCount = 0;
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new LeagueClientLauncher(
                () => false,
                LeagueClientLaunchTarget.ExternalLauncherRequired,
                _ =>
                {
                    startCount++;
                    return new Process();
                },
                logger);

            var result = await launcher.LaunchAsync();

            Assert.Equal(
                LeagueClientLaunchStatus.ExternalLauncherRequired, result);
            Assert.Equal(0, startCount);
        }

        [Fact]
        public async Task LaunchAsync_WhenLeagueClientIsRunning_DoesNotStartAnotherProcess()
        {
            var startCount = 0;
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new LeagueClientLauncher(
                () => true,
                () => throw new InvalidOperationException("Should not resolve"),
                _ =>
                {
                    startCount++;
                    return new Process();
                },
                logger);

            var result = await launcher.LaunchAsync();

            Assert.Equal(LeagueClientLaunchStatus.AlreadyRunning, result);
            Assert.Equal(0, startCount);
        }

        [Fact]
        public async Task LaunchAsync_WhenGameLauncherCannotBeResolved_ReturnsNotFound()
        {
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new LeagueClientLauncher(
                () => false,
                () => null,
                _ => throw new InvalidOperationException("Should not start"),
                logger);

            var result = await launcher.LaunchAsync();

            Assert.Equal(LeagueClientLaunchStatus.LauncherNotFound, result);
        }

        [Fact]
        public async Task LaunchAsync_WhenProcessStartFails_ReturnsFailed()
        {
            const string riotClientPath =
                @"D:\Riot Games\Riot Client\RiotClientServices.exe";
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new LeagueClientLauncher(
                () => false,
                () => LeagueClientLauncher.CreateLaunchTarget(
                    riotClientPath,
                    path => string.Equals(
                        path, riotClientPath, StringComparison.OrdinalIgnoreCase)),
                _ => throw new System.ComponentModel.Win32Exception("blocked"),
                logger);

            var result = await launcher.LaunchAsync();

            Assert.Equal(LeagueClientLaunchStatus.Failed, result);
        }
    }
}
